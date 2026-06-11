using System;
using System.IO;
using UnityEngine;
using UnityEngine.Video;

public class VideoPlayerManager : MonoBehaviour
{
    [SerializeField] private VideoPlayer videoPlayer;
    [Tooltip("대기 화면에서 멈춰서 보여줄 지점(초). 영상이 검은 화면으로 시작하면 장면이 보이는 지점으로 조절")]
    [SerializeField] private double standbyTime = 0.01;

    // 사용자가 교체 가능한 비디오 확장자 목록
    private static readonly string[] SupportedExtensions = { ".mp4", ".mov", ".webm", ".avi", ".m4v", ".wmv" };

    private string videoAPath; // StreamingAssets/video/video1.* -> A
    private string videoBPath; // StreamingAssets/video/video2.* -> B

    private void Awake()
    {
        Screen.SetResolution(1920, 1080, FullScreenMode.FullScreenWindow);

        if (videoPlayer == null)
        {
            videoPlayer = GetComponent<VideoPlayer>();
        }

        videoPlayer.playOnAwake = false;
        videoPlayer.isLooping = false; // 루프 없음
        videoPlayer.source = VideoSource.Url;
        videoPlayer.renderMode = VideoRenderMode.CameraNearPlane;
        videoPlayer.targetCamera = Camera.main;
        videoPlayer.aspectRatio = VideoAspectRatio.FitInside;
        videoPlayer.waitForFirstFrame = true;

        videoPlayer.loopPointReached += OnVideoFinished;
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

        videoPlayer.frameReady -= OnStandbyFrameReady;
        videoPlayer.sendFrameReadyEvents = false;
        videoPlayer.prepareCompleted -= OnPrepared;

        string url = ToFileUrl(path);
        if (videoPlayer.url == url && videoPlayer.isPrepared)
        {
            // 이미 로드된 영상이면 다시 준비하지 않고 대기 프레임으로 되감기만 한다
            PauseAtStandbyFrame(videoPlayer);
            return;
        }

        videoPlayer.Stop();
        videoPlayer.url = url;
        videoPlayer.prepareCompleted += OnPrepared;
        videoPlayer.Prepare();
    }

    private void OnPrepared(VideoPlayer vp)
    {
        vp.prepareCompleted -= OnPrepared;
        PauseAtStandbyFrame(vp);
    }

    // 대기 프레임이 실제로 그려진 뒤 멈춰야 투명/검은 화면이 안 보인다
    private void PauseAtStandbyFrame(VideoPlayer vp)
    {
        vp.sendFrameReadyEvents = true;
        vp.frameReady += OnStandbyFrameReady;
        vp.time = standbyTime;
        vp.Play();
    }

    private void OnStandbyFrameReady(VideoPlayer vp, long frameIndex)
    {
        vp.frameReady -= OnStandbyFrameReady;
        vp.sendFrameReadyEvents = false;
        vp.Pause(); // 대기 프레임만 표시하고 정지
    }

    // Space: 재생 중이면 일시정지, 멈춰 있으면 현재 위치에서 재생
    private void TogglePlayPause()
    {
        if (!videoPlayer.isPrepared)
        {
            return;
        }

        // 대기 프레임 정지 예약이 남아 있으면 해제 (Space가 우선)
        videoPlayer.frameReady -= OnStandbyFrameReady;
        videoPlayer.sendFrameReadyEvents = false;

        if (videoPlayer.isPlaying)
        {
            videoPlayer.Pause();
        }
        else
        {
            videoPlayer.Play();
        }
    }

    // 재생이 끝나면(루프 없음) 현재 영상의 대기 화면으로 복귀
    private void OnVideoFinished(VideoPlayer vp)
    {
        PauseAtStandbyFrame(vp);
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

    private static string ToFileUrl(string path)
    {
        return new Uri(path).AbsoluteUri;
    }
}
