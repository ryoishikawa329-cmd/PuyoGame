using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace PuyoGame
{
    /// <summary>
    /// ブラウザは、利用者が画面を触るまで音を鳴らさない。
    /// 最初のタップ・クリック・キー入力まで音を止めておき、その後に鳴らし直す。
    /// WebGL 以外では制限がないので、最初から解除された状態で始まる。
    /// </summary>
    [DisallowMultipleComponent]
    public class WebAudioUnlock : MonoBehaviour
    {
        [Tooltip("解除したときに鳴らし直すBGM。未設定でも動く")]
        [SerializeField] MusicPlayer music;

        /// <summary>音を鳴らしてよい状態になったか。</summary>
        public bool IsUnlocked { get; private set; }

        /// <summary>ブラウザの制限を受ける環境か（動作確認用）。</summary>
        public static bool NeedsUnlock => Application.platform == RuntimePlatform.WebGLPlayer;

        void OnEnable()
        {
            IsUnlocked = !NeedsUnlock;
            AudioListener.pause = !IsUnlocked;
        }

        void OnDisable()
        {
            AudioListener.pause = false;      // 止めたまま抜けないようにする
        }

        void Update()
        {
            if (IsUnlocked) return;
            if (AnyInput()) Unlock();
        }

        /// <summary>
        /// 音の停止を解除する。
        /// index.html からも SendMessage で呼ばれるので、名前を変えるときは両方直すこと。
        /// </summary>
        public void UnlockFromBrowser() => Unlock();

        /// <summary>音の停止を解除して、止まっている間に始まった曲を鳴らし直す。</summary>
        public void Unlock()
        {
            if (IsUnlocked) return;

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
