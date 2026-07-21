using System;
using System.Collections;
using System.Runtime.InteropServices;
using UnityEngine;

public class UnityAlwaysOnTop : MonoBehaviour
{
    [DllImport("user32.dll")]
    private static extern IntPtr FindWindow(string lpClassName, string lpWindowName);

    // 현재 프로세스의 활성 창 핸들 (제목으로 찾는 FindWindow보다 안정적)
    [DllImport("user32.dll")]
    private static extern IntPtr GetActiveWindow();

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter,
        int X, int Y, int cx, int cy, uint uFlags);

    private static readonly IntPtr HWND_TOPMOST = new IntPtr(-1);
    private const UInt32 SWP_NOSIZE = 0x0001;
    private const UInt32 SWP_NOMOVE = 0x0002;
    private const UInt32 SWP_NOACTIVATE = 0x0010;
    private const UInt32 TOPMOST_FLAGS = SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE;

    [SerializeField] private Camera cam1;
    [SerializeField] private Camera cam2;
    [SerializeField] private Camera cam3;

    [Tooltip("전체화면 전환이 끝난 뒤 적용해야 해서 지연을 둔다(초)")]
    [SerializeField] private float applyDelay = 3f;

    private bool applied;

    void Start()
    {
        if (Application.isEditor)
        {
            Debug.Log("에디터에서는 AlwaysOnTop 설정 생략");
            return;
        }

        // 디스플레이 활성화는 가능한 한 빨리 해야 한다
        if (Display.displays.Length > 1)
        {
            Display.displays[1].Activate();
        }
        if (Display.displays.Length > 2)
        {
            Display.displays[2].Activate();
        }

        StartCoroutine(ApplyAfterDelay());
    }

    // 전체화면 전환(Screen.SetResolution)이 비동기라 곧바로 적용하면 커서 숨김이 되돌려진다
    private IEnumerator ApplyAfterDelay()
    {
        yield return new WaitForSecondsRealtime(applyDelay);

        HideCursor();

        if (!JsonManager.instance.gameSettingData.useUnityOnTop)
        {
            yield break;
        }

        SetTopMost();
        applied = true;
    }

    private void HideCursor()
    {
        Cursor.visible = false;
    }

    private void SetTopMost()
    {
        // 창 제목은 시작 직후 아직 productName이 아닐 수 있어 활성 창 핸들을 우선 사용한다
        IntPtr hWnd = GetActiveWindow();
        if (hWnd == IntPtr.Zero)
        {
            hWnd = FindWindow(null, Application.productName);
        }

        if (hWnd == IntPtr.Zero)
        {
            Debug.LogError("Unity 창 핸들을 찾지 못했습니다.");
            return;
        }

        if (SetWindowPos(hWnd, HWND_TOPMOST, 0, 0, 0, 0, TOPMOST_FLAGS))
        {
            Debug.Log("🪟빌드 실행파일에서 Unity 창을 항상 위로 설정했습니다.");
        }
        else
        {
            Debug.LogError("SetWindowPos 실패: " + Marshal.GetLastWin32Error());
        }
    }

    // 알트탭 등으로 포커스가 돌아오면 커서 숨김과 최상단이 풀리므로 다시 적용한다
    private void OnApplicationFocus(bool hasFocus)
    {
        if (!hasFocus || Application.isEditor)
        {
            return;
        }

        HideCursor();

        if (applied)
        {
            SetTopMost();
        }
    }
}
