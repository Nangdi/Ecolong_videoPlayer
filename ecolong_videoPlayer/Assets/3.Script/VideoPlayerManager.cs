using System;
using System.IO;
using UnityEngine;
using RenderHeads.Media.AVProVideo;

public class VideoPlayerManager : MonoBehaviour
{
    [SerializeField] private MediaPlayer mediaPlayer;
    [Tooltip("영상을 표시할 DisplayUGUI (풀스크린 Canvas 하위)")]
    [SerializeField] private DisplayUGUI display;
    [Tooltip("대기 화면에서 멈춰서 보여줄 지점(초). 영상이 검은 화면으로 시작하면 장면이 보이는 지점으로 조절")]
    [SerializeField] private double standbyTime = 0.01;

    // 사용자가 교체 가능한 비디오 확장자 목록
    private static readonly string[] SupportedExtensions = { ".mp4", ".mov", ".webm", ".avi", ".m4v", ".wmv" };

    private string videoAPath; // StreamingAssets/video/video1.* -> A
    private string videoBPath; // StreamingAssets/video/video2.* -> B

    private string currentPath;   // 현재 열려 있는 영상 경로
    private bool pendingStandby;  // 로드 완료 후 대기 프레임으로 이동해야 하는 상태

    private void Awake()
    {
        Screen.SetResolution(1920, 1080, FullScreenMode.FullScreenWindow);

        if (mediaPlayer == null)
        {
            mediaPlayer = GetComponent<MediaPlayer>();
        }

        // 인스펙터에 지정된 영상이 Start()에서 자동 재생되지 않도록 먼저 꺼둔다
        mediaPlayer.AutoOpen = false;
        mediaPlayer.AutoStart = false;
        mediaPlayer.Loop = false; // 루프 없음

        mediaPlayer.Events.AddListener(OnMediaPlayerEvent);

        if (display != null)
        {
            display.Player = mediaPlayer;
        }
    }

    private void OnDestroy()
    {
        if (mediaPlayer != null)
        {
            mediaPlayer.Events.RemoveListener(OnMediaPlayerEvent);
        }
    }

    private void Start()
    {
        string videoDir = Path.Combine(Application.streamingAssetsPath, "video");
        videoAPath = FindVideoFile(videoDir, "video1");
        videoBPath = FindVideoFile(videoDir, "video2");

        if (videoAPath == null)
        {
            Debug.LogError($"video1 파일을 찾을 수 없습니다: {videoDir}");
        }
        if (videoBPath == null)
        {
            Debug.LogError($"video2 파일을 찾을 수 없습니다: {videoDir}");
        }

        ShowStandby(videoAPath);
    }

    private void Update()
    {
        if (Input.GetKeyDown(KeyCode.Alpha1) || Input.GetKeyDown(KeyCode.Keypad1))
        {
            ShowStandby(videoAPath); // A 영상 대기
        }
        else if (Input.GetKeyDown(KeyCode.Alpha2) || Input.GetKeyDown(KeyCode.Keypad2))
        {
            ShowStandby(videoBPath); // B 영상 대기
        }
        else if (Input.GetKeyDown(KeyCode.Space))
        {
            TogglePlayPause(); // 재생/정지 토글
        }
    }

    // 선택한 영상을 로드하고 대기 지점 프레임에서 멈춰서 보여준다
    private void ShowStandby(string path)
    {
        if (path == null)
        {
            return;
        }

        if (currentPath == path && mediaPlayer.MediaOpened)
        {
            // 이미 로드된 영상이면 다시 열지 않고 대기 프레임으로 되감기만 한다
            SeekToStandby();
            return;
        }

        currentPath = path;
        pendingStandby = true; // 로드가 끝나면 대기 프레임으로 이동
        mediaPlayer.OpenMedia(new MediaPath(path, MediaPathType.AbsolutePathOrURL), autoPlay: false);
    }

    // 재생을 멈춘 채 대기 지점으로 탐색한다 (AVPro는 일시정지 상태에서도 탐색한 프레임을 그린다)
    private void SeekToStandby()
    {
        IMediaControl control = mediaPlayer.Control;
        if (control == null)
        {
            return;
        }

        pendingStandby = false;
        control.Pause();
        control.Seek(standbyTime);
    }

    // Space: 재생 중이면 일시정지, 멈춰 있으면 현재 위치에서 재생
    private void TogglePlayPause()
    {
        IMediaControl control = mediaPlayer.Control;
        if (control == null || !control.CanPlay())
        {
            return;
        }

        pendingStandby = false; // 대기 프레임 이동 예약이 남아 있으면 해제 (Space가 우선)

        if (control.IsPlaying())
        {
            control.Pause();
        }
        else
        {
            control.Play();
        }
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

            // 재생이 끝나면(루프 없음) 현재 영상의 대기 화면으로 복귀
            case MediaPlayerEvent.EventType.FinishedPlaying:
                SeekToStandby();
                break;

            case MediaPlayerEvent.EventType.Error:
                Debug.LogError($"영상 재생 오류 ({errorCode}): {currentPath}");
                break;
        }
    }

    // video/{baseName}.{지원 확장자} 파일을 찾는다 (사용자가 파일을 교체할 수 있음)
    private string FindVideoFile(string directory, string baseName)
    {
        if (!Directory.Exists(directory))
        {
            return null;
        }

        foreach (string file in Directory.GetFiles(directory, baseName + ".*"))
        {
            string ext = Path.GetExtension(file).ToLowerInvariant();
            if (Array.IndexOf(SupportedExtensions, ext) >= 0)
            {
                return file;
            }
        }
        return null;
    }
}
