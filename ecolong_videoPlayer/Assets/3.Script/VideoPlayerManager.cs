using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.UI;
using RenderHeads.Media.AVProVideo;

// MediaPlayer 하나와 재생목록 하나를 묶어서
// 대기 프레임 표시 / 재생·정지 / 다음 영상 자동 재생 / _loop 무한 반복을 처리한다
public class VideoChannel
{
    private readonly MediaPlayer player;
    private readonly double standbyTime;

    private List<string> clips = new List<string>();
    private int index = -1;
    private string currentPath;  // 현재 열려 있는 영상 경로
    private bool pendingStandby; // 로드 완료 후 대기 프레임으로 이동해야 하는 상태

    public VideoChannel(MediaPlayer player, double standbyTime)
    {
        this.player = player;
        this.standbyTime = standbyTime;

        // 인스펙터에 지정된 영상이 Start()에서 자동 재생되지 않도록 먼저 꺼둔다
        player.AutoOpen = false;
        player.AutoStart = false;
        player.Loop = false; // 실제 루프 여부는 영상마다 결정

        player.Events.AddListener(OnMediaPlayerEvent);
    }

    public bool HasClips => clips.Count > 0;

    public bool IsPlaying
    {
        get
        {
            IMediaControl control = player.Control;
            return control != null && control.IsPlaying();
        }
    }

    // 재생목록을 교체하고 첫 영상의 대기 화면을 보여준다
    public void SetPlaylist(List<string> newClips)
    {
        clips = newClips ?? new List<string>();

        if (clips.Count == 0)
        {
            Stop();
            return;
        }

        LoadClip(0, false);
    }

    public void Stop()
    {
        pendingStandby = false;
        currentPath = null;
        index = -1;
        player.CloseMedia();
    }

    public void Play()
    {
        IMediaControl control = player.Control;
        if (control == null || !control.CanPlay())
        {
            return;
        }

        pendingStandby = false; // 대기 프레임 이동 예약이 남아 있으면 해제 (재생이 우선)
        control.Play();
    }

    public void Pause()
    {
        IMediaControl control = player.Control;
        if (control == null || !control.CanPlay())
        {
            return;
        }

        pendingStandby = false;
        control.Pause();
    }

    public void TogglePlayPause()
    {
        if (IsPlaying)
        {
            Pause();
        }
        else
        {
            Play();
        }
    }

    public void Dispose()
    {
        if (player != null)
        {
            player.Events.RemoveListener(OnMediaPlayerEvent);
        }
    }

    // 재생목록의 clipIndex 번째 영상을 연다
    // autoPlay = false : 대기 지점 프레임에서 멈춰서 보여줌 / true : 열리는 대로 바로 재생
    private void LoadClip(int clipIndex, bool autoPlay)
    {
        if (clipIndex < 0 || clipIndex >= clips.Count)
        {
            return;
        }

        index = clipIndex;

        string path = clips[clipIndex];
        player.Loop = VideoPlayerManager.IsLoopClip(path); // 이름이 _loop 로 끝나면 이 영상만 무한 반복

        if (currentPath == path && player.MediaOpened)
        {
            // 이미 열려 있는 영상이면 다시 열지 않는다
            IMediaControl control = player.Control;
            if (control == null)
            {
                return;
            }

            if (autoPlay)
            {
                pendingStandby = false;
                control.Rewind();
                control.Play();
            }
            else
            {
                SeekToStandby();
            }
            return;
        }

        currentPath = path;
        pendingStandby = !autoPlay; // 로드가 끝나면 대기 프레임으로 이동
        player.OpenMedia(new MediaPath(path, MediaPathType.AbsolutePathOrURL), autoPlay);
    }

    // 재생을 멈춘 채 대기 지점으로 탐색한다 (AVPro는 일시정지 상태에서도 탐색한 프레임을 그린다)
    private void SeekToStandby()
    {
        IMediaControl control = player.Control;
        if (control == null)
        {
            return;
        }

        pendingStandby = false;
        control.Pause();
        control.Seek(standbyTime);
    }

    private void OnMediaPlayerEvent(MediaPlayer mp, MediaPlayerEvent.EventType et, ErrorCode errorCode)
    {
        switch (et)
        {
            case MediaPlayerEvent.EventType.ReadyToPlay:
                if (pendingStandby)
                {
                    SeekToStandby();
                }
                break;

            case MediaPlayerEvent.EventType.FinishedPlaying:
                OnClipFinished();
                break;

            case MediaPlayerEvent.EventType.Error:
                Debug.LogError($"영상 재생 오류 ({errorCode}): {currentPath}");
                break;
        }
    }

    // 한 영상이 끝나면 다음 영상을 이어서 재생, 마지막이면 첫 영상 대기 화면으로 복귀
    private void OnClipFinished()
    {
        if (player.Loop)
        {
            // 루프 지정 영상은 AVPro가 알아서 처음으로 되감아 계속 재생한다
            return;
        }

        if (index + 1 < clips.Count)
        {
            LoadClip(index + 1, true); // 다음 영상 자동 재생
        }
        else
        {
            LoadClip(0, false); // 마지막 영상 종료 -> 첫 영상 대기
        }
    }
}

public class VideoPlayerManager : MonoBehaviour
{
    [SerializeField] private MediaPlayer mediaPlayer;
    [Tooltip("영상을 표시할 DisplayUGUI (풀스크린 Canvas 하위)")]
    [SerializeField] private DisplayUGUI display;
    [Tooltip("대기 화면에서 멈춰서 보여줄 지점(초). 영상이 검은 화면으로 시작하면 장면이 보이는 지점으로 조절")]
    [SerializeField] private double standbyTime = 0.01;
    [Tooltip("체크하면 Q 비교 화면의 좌우 영상이 비율을 무시하고 칸을 꽉 채운다. 해제하면 원본 비율 유지")]
    [SerializeField] private bool compareStretchToFill = true;
    [Tooltip("비교 화면에서 좌우 영상이 동시에 소리를 내지 않도록 음소거")]
    [SerializeField] private bool muteCompareAudio = true;

    // StreamingAssets/video 아래의 숫자 폴더 개수 (1 ~ 5)
    private const int SetCount = 5;

    // 비교 화면(Q)에서 좌 / 우에 재생할 폴더 이름
    private const string LeftFolderName = "team1";
    private const string RightFolderName = "team2";

    // 사용자가 교체 가능한 비디오 확장자 목록
    private static readonly string[] SupportedExtensions = { ".mp4", ".mov", ".webm", ".avi", ".m4v", ".wmv" };

    // 파일 이름이 이 접미사로 끝나면 그 영상만 무한 반복한다 (예: 01_intro_loop.mp4)
    private const string LoopSuffix = "_loop";

    private static readonly KeyCode[] AlphaKeys =
    {
        KeyCode.Alpha1, KeyCode.Alpha2, KeyCode.Alpha3, KeyCode.Alpha4, KeyCode.Alpha5
    };

    private static readonly KeyCode[] KeypadKeys =
    {
        KeyCode.Keypad1, KeyCode.Keypad2, KeyCode.Keypad3, KeyCode.Keypad4, KeyCode.Keypad5
    };

    // videoSets[i] = StreamingAssets/video/(i+1) 폴더의 영상 목록 (이름순)
    private readonly List<string>[] videoSets = new List<string>[SetCount];
    private List<string> leftClips = new List<string>();  // team1
    private List<string> rightClips = new List<string>(); // team2

    private VideoChannel mainChannel;  // 1 ~ 5 전체 화면 재생
    private VideoChannel leftChannel;  // 비교 화면 왼쪽 (team1)
    private VideoChannel rightChannel; // 비교 화면 오른쪽 (team2)

    private GameObject compareCanvasRoot;

    private int currentSetIndex = -1; // 현재 선택된 숫자 폴더
    private bool compareMode;         // Q 비교 화면 표시 중인지

    private void Awake()
    {
        Screen.SetResolution(1920, 1080, FullScreenMode.FullScreenWindow);

        if (mediaPlayer == null)
        {
            mediaPlayer = GetComponent<MediaPlayer>();
        }

        mainChannel = new VideoChannel(mediaPlayer, standbyTime);

        if (display != null)
        {
            display.Player = mediaPlayer;
        }

        BuildCompareView();
    }

    private void Start()
    {
        string videoRoot = Path.Combine(Application.streamingAssetsPath, "video");

        for (int i = 0; i < SetCount; i++)
        {
            string setDir = Path.Combine(videoRoot, (i + 1).ToString());
            videoSets[i] = CollectVideoFiles(setDir);

            if (videoSets[i].Count == 0)
            {
                Debug.LogWarning($"{i + 1}번 폴더에 재생할 영상이 없습니다: {setDir}");
            }
        }

        leftClips = CollectVideoFiles(Path.Combine(videoRoot, LeftFolderName));
        rightClips = CollectVideoFiles(Path.Combine(videoRoot, RightFolderName));

        if (leftClips.Count == 0)
        {
            Debug.LogWarning($"{LeftFolderName} 폴더에 재생할 영상이 없습니다.");
        }
        if (rightClips.Count == 0)
        {
            Debug.LogWarning($"{RightFolderName} 폴더에 재생할 영상이 없습니다.");
        }

        SelectSet(0);
    }

    private void Update()
    {
        if (Input.GetKeyDown(KeyCode.Q))
        {
            if (compareMode)
            {
                ExitCompareMode(); // 다시 Q -> 원래 폴더 대기 화면으로
            }
            else
            {
                EnterCompareMode();
            }
            return;
        }

        for (int i = 0; i < SetCount; i++)
        {
            if (Input.GetKeyDown(AlphaKeys[i]) || Input.GetKeyDown(KeypadKeys[i]))
            {
                SelectSet(i); // 해당 폴더의 첫 영상 대기
                return;
            }
        }

        if (Input.GetKeyDown(KeyCode.Space))
        {
            TogglePlayPause(); // 재생/정지 토글
        }
    }

    private void OnDestroy()
    {
        mainChannel?.Dispose();
        leftChannel?.Dispose();
        rightChannel?.Dispose();
    }

    // 숫자 폴더를 선택하고 그 폴더의 첫 영상 대기 화면을 보여준다
    private void SelectSet(int setIndex)
    {
        if (setIndex < 0 || setIndex >= SetCount)
        {
            return;
        }

        List<string> clips = videoSets[setIndex];
        if (clips == null || clips.Count == 0)
        {
            Debug.LogWarning($"{setIndex + 1}번 폴더에 재생할 영상이 없어 선택할 수 없습니다.");
            return;
        }

        HideCompareView();
        currentSetIndex = setIndex;
        mainChannel.SetPlaylist(clips);
    }

    // Space: 비교 화면에서는 좌우를 함께, 단일 화면에서는 현재 영상을 토글
    private void TogglePlayPause()
    {
        if (!compareMode)
        {
            mainChannel.TogglePlayPause();
            return;
        }

        if (leftChannel.IsPlaying || rightChannel.IsPlaying)
        {
            leftChannel.Pause();
            rightChannel.Pause();
        }
        else
        {
            leftChannel.Play();
            rightChannel.Play();
        }
    }

    // Q: team1 / team2 영상을 좌우로 나란히 띄워 비교한다
    private void EnterCompareMode()
    {
        if (leftClips.Count == 0 && rightClips.Count == 0)
        {
            Debug.LogWarning($"{LeftFolderName} / {RightFolderName} 폴더에 영상이 없어 비교 화면을 열 수 없습니다.");
            return;
        }

        compareMode = true;
        mainChannel.Stop(); // 전체 화면 재생 중지
        compareCanvasRoot.SetActive(true);

        leftChannel.SetPlaylist(leftClips);
        rightChannel.SetPlaylist(rightClips);
    }

    // 비교 화면을 닫고 마지막으로 선택했던 숫자 폴더의 대기 화면으로 돌아간다
    private void ExitCompareMode()
    {
        HideCompareView();

        if (currentSetIndex >= 0)
        {
            mainChannel.SetPlaylist(videoSets[currentSetIndex]);
        }
    }

    private void HideCompareView()
    {
        if (!compareMode)
        {
            return;
        }

        compareMode = false;
        leftChannel.Stop();
        rightChannel.Stop();
        compareCanvasRoot.SetActive(false);
    }

    // 비교 화면용 캔버스 / MediaPlayer / DisplayUGUI 를 코드로 생성한다 (씬 세팅 불필요)
    private void BuildCompareView()
    {
        compareCanvasRoot = new GameObject("CompareCanvas");
        compareCanvasRoot.SetActive(false);
        compareCanvasRoot.transform.SetParent(transform, false);

        Canvas canvas = compareCanvasRoot.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 100; // 전체 화면 영상(VideoCanvas) 위에 그린다

        CanvasScaler scaler = compareCanvasRoot.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
        scaler.matchWidthOrHeight = 0.5f;

        // 좌우 영상 바깥 여백을 검은색으로 덮는다
        var background = new GameObject("Background", typeof(RectTransform), typeof(Image));
        background.transform.SetParent(compareCanvasRoot.transform, false);
        StretchToParent(background.GetComponent<RectTransform>());
        background.GetComponent<Image>().color = Color.black;

        leftChannel = CreateCompareChannel("Left", 0f, 0.5f);
        rightChannel = CreateCompareChannel("Right", 0.5f, 1f);
    }

    // 화면을 좌우로 나눈 한쪽 칸에 DisplayUGUI 를 배치하고 전용 MediaPlayer 를 만든다
    private VideoChannel CreateCompareChannel(string name, float anchorMinX, float anchorMaxX)
    {
        // 화면의 절반을 차지하는 칸
        var half = new GameObject(name + "Half", typeof(RectTransform));
        half.transform.SetParent(compareCanvasRoot.transform, false);

        RectTransform halfRect = half.GetComponent<RectTransform>();
        halfRect.anchorMin = new Vector2(anchorMinX, 0f);
        halfRect.anchorMax = new Vector2(anchorMaxX, 1f);
        halfRect.offsetMin = Vector2.zero;
        halfRect.offsetMax = Vector2.zero;

        // MediaPlayer 는 캔버스와 별개로 항상 활성 상태여야 이벤트가 정상 동작한다
        var playerObject = new GameObject(name + "MediaPlayer");
        playerObject.SetActive(false);
        playerObject.transform.SetParent(transform, false);

        MediaPlayer player = playerObject.AddComponent<MediaPlayer>();
        player.AutoOpen = false;
        player.AutoStart = false;
        player.AudioMuted = muteCompareAudio; // 좌우 소리가 겹치지 않도록
        playerObject.SetActive(true);

        var view = new GameObject(name + "Video");
        view.SetActive(false);
        view.transform.SetParent(half.transform, false);
        StretchToParent(view.AddComponent<RectTransform>());

        DisplayUGUI videoDisplay = view.AddComponent<DisplayUGUI>();
        videoDisplay.Player = player;
        // StretchToFill : 비율 무시하고 칸을 꽉 채움 (1920x1080 영상도 위아래로 늘어남)
        // ScaleToFit    : 원본 비율 유지 (위아래 또는 좌우에 여백 발생)
        videoDisplay.ScaleMode = compareStretchToFill
            ? UnityEngine.ScaleMode.StretchToFill
            : UnityEngine.ScaleMode.ScaleToFit;
        view.SetActive(true);

        return new VideoChannel(player, standbyTime);
    }

    private static void StretchToParent(RectTransform rect)
    {
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;
    }

    // 폴더 안의 지원 확장자 영상들을 이름순(숫자 자연 정렬)으로 모은다
    private static List<string> CollectVideoFiles(string directory)
    {
        var files = new List<string>();
        if (!Directory.Exists(directory))
        {
            return files;
        }

        foreach (string file in Directory.GetFiles(directory))
        {
            string ext = Path.GetExtension(file).ToLowerInvariant();
            if (Array.IndexOf(SupportedExtensions, ext) >= 0)
            {
                files.Add(file);
            }
        }

        files.Sort((a, b) => CompareNatural(Path.GetFileName(a), Path.GetFileName(b)));
        return files;
    }

    // 확장자를 뺀 파일 이름이 _loop 로 끝나는지 검사한다 (대소문자 무시)
    public static bool IsLoopClip(string path)
    {
        string name = Path.GetFileNameWithoutExtension(path);
        return name != null && name.EndsWith(LoopSuffix, StringComparison.OrdinalIgnoreCase);
    }

    // "video2" 가 "video10" 보다 앞에 오도록 숫자 구간은 값으로 비교한다
    private static int CompareNatural(string a, string b)
    {
        int i = 0;
        int j = 0;

        while (i < a.Length && j < b.Length)
        {
            if (char.IsDigit(a[i]) && char.IsDigit(b[j]))
            {
                int startA = i;
                int startB = j;
                while (i < a.Length && char.IsDigit(a[i])) i++;
                while (j < b.Length && char.IsDigit(b[j])) j++;

                string numA = a.Substring(startA, i - startA).TrimStart('0');
                string numB = b.Substring(startB, j - startB).TrimStart('0');

                if (numA.Length != numB.Length)
                {
                    return numA.Length - numB.Length;
                }

                int numCompare = string.CompareOrdinal(numA, numB);
                if (numCompare != 0)
                {
                    return numCompare;
                }
            }
            else
            {
                int charCompare = char.ToLowerInvariant(a[i]).CompareTo(char.ToLowerInvariant(b[j]));
                if (charCompare != 0)
                {
                    return charCompare;
                }
                i++;
                j++;
            }
        }

        return (a.Length - i) - (b.Length - j);
    }
}
