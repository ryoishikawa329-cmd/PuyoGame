using System.Runtime.InteropServices;
using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace PuyoGame
{
    /// <summary>
    /// ブラウザは、利用者が画面を触るまで音を鳴らさない。
    /// 特に iOS Safari は AudioContext を止めたままにするので、
    /// 最初のタップで resume() を呼び、鳴るようになってからBGMを流し直す。
    ///
    /// 解除のきっかけは2経路ある。
    ///   - このスクリプトが毎フレーム入力を見る（保険）
    ///   - index.html がタップの処理の中で直接 resume する（本命）
    /// ブラウザは「利用者の操作の中で呼ばれたか」を見るため、後者のほうが確実。
    /// </summary>
    [DisallowMultipleComponent]
    public class WebAudioUnlock : MonoBehaviour
    {
        [Tooltip("解除したときに鳴らし直すBGM。未設定でも動く")]
        [SerializeField] MusicPlayer music;
        [Tooltip("解除後も、音が止まっていないかを見張る間隔（秒）")]
        [Min(0.1f)]
        [SerializeField] float watchInterval = 1f;

        /// <summary>音を鳴らしてよい状態になったか。</summary>
        public bool IsUnlocked { get; private set; }

        /// <summary>直前に調べた AudioContext の状態（動作確認用）。</summary>
        public int LastAudioState { get; private set; }

        /// <summary>ブラウザの制限を受ける環境か。</summary>
        public static bool NeedsUnlock =>
            Application.platform == RuntimePlatform.WebGLPlayer && !Application.isEditor;

        float watchTimer;

#if UNITY_WEBGL && !UNITY_EDITOR
        [DllImport("__Internal")] static extern void PuyoAudioBridgeInit();
        [DllImport("__Internal")] static extern int PuyoAudioResume();
        [DllImport("__Internal")] static extern int PuyoAudioIsRunning();
#else
        // エディタや実機ビルドには制限がないので、常に「鳴らせる」を返す
        static void PuyoAudioBridgeInit() { }
        static int PuyoAudioResume() => 1;
        static int PuyoAudioIsRunning() => 1;
#endif

        void OnEnable()
        {
            PuyoAudioBridgeInit();

            IsUnlocked = !NeedsUnlock;
            AudioListener.pause = !IsUnlocked;
            watchTimer = 0f;
        }

        void OnDisable()
        {
            AudioListener.pause = false;      // 止めたまま抜けないようにする
        }

        void Update()
        {
            if (!IsUnlocked)
            {
                if (AnyInput()) Unlock();
                return;
            }

            // 一度鳴り出しても、タブを離れると再び止められることがある
            watchTimer += Time.unscaledDeltaTime;
            if (watchTimer < watchInterval) return;
            watchTimer = 0f;

            LastAudioState = PuyoAudioIsRunning();
            if (LastAudioState == 0) PuyoAudioResume();
        }

        /// <summary>
        /// 音の停止を解除する。
        /// index.html からも SendMessage で呼ばれるので、名前を変えるときは両方直すこと。
        /// </summary>
        public void UnlockFromBrowser() => Unlock();

        /// <summary>音を鳴らせる状態にして、止まっている間に始まった曲を鳴らし直す。</summary>
        public void Unlock()
        {
            if (IsUnlocked) return;

            LastAudioState = PuyoAudioResume();
            // -1（音声機構が見つからない）のときは、判断材料が無いので鳴らしてみる
            if (LastAudioState == 0) return;      // まだ止まっている。次のタップに賭ける

            IsUnlocked = true;
            AudioListener.pause = false;
            if (music != null) music.Replay();
        }

        static bool AnyInput()
        {
#if ENABLE_INPUT_SYSTEM
            var pointer = Pointer.current;
            if (pointer != null && pointer.press.wasPressedThisFrame) return true;

            var touch = Touchscreen.current;
            if (touch != null && touch.primaryTouch.press.wasPressedThisFrame) return true;

            var keyboard = Keyboard.current;
            return keyboard != null && keyboard.anyKey.wasPressedThisFrame;
#elif ENABLE_LEGACY_INPUT_MANAGER
            return Input.anyKeyDown || Input.touchCount > 0;
#else
            return false;
#endif
        }
    }
}
