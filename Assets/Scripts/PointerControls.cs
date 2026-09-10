using UnityEngine;
using UnityEngine.EventSystems;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace PuyoGame
{
    /// <summary>
    /// タップ・クリック・なぞり操作でペアを動かす。
    /// キーボードの無いブラウザやスマホでも遊べるようにするためのもので、
    /// キー操作（PairFallController 側）とは併用できる。
    ///
    /// 画面を縦3分割し、左をタップで左へ、右をタップで右へ、中央をタップで回転。
    /// 下へなぞると1マスずつ落ち、一気に大きく下へ払うと一番下まで落ちる。
    /// ゲームオーバー中はどこをタップしても再開する。
    /// </summary>
    [DisallowMultipleComponent]
    public class PointerControls : MonoBehaviour
    {
        [SerializeField] PairFallController controller;
        [SerializeField] GameManager gameManager;

        [Header("判定")]
        [Tooltip("タップとみなす最大の秒数")]
        [Min(0.05f)]
        [SerializeField] float tapMaxSeconds = 0.4f;
        [Tooltip("タップとみなす最大の指の動き（画面の短い方に対する割合）")]
        [Range(0.005f, 0.2f)]
        [SerializeField] float tapMaxDrag = 0.035f;
        [Tooltip("下へこの割合なぞるごとに1マス落とす")]
        [Range(0.01f, 0.3f)]
        [SerializeField] float dragStep = 0.05f;
        [Tooltip("下へ一気にこの割合なぞると、一番下まで落とす")]
        [Range(0.1f, 1f)]
        [SerializeField] float flickToDrop = 0.28f;
        [Tooltip("左右の操作領域の幅（画面幅に対する割合）。残りの中央が回転になる")]
        [Range(0.15f, 0.5f)]
        [SerializeField] float sideZone = 0.34f;

        bool tracking;
        bool dragged;
        bool droppedThisPress;
        GameState pressState;
        float pressTime;
        Vector2 pressPosition;
        Vector2 stepAnchor;

        /// <summary>直前に受け取った操作（動作確認用）。</summary>
        public string LastAction { get; private set; } = string.Empty;

        void Reset()
        {
            controller = GetComponent<PairFallController>();
            gameManager = GetComponent<GameManager>();
        }

        void Awake()
        {
            if (controller == null) controller = GetComponent<PairFallController>();
            if (gameManager == null) gameManager = GetComponent<GameManager>();
        }

        void Update()
        {
            if (!TryReadPointer(out var position, out bool pressedNow,
                                out bool held, out bool releasedNow)) return;

            if (pressedNow)
            {
                // UI（STARTボタンなど）の上で押したときは、そちらに任せる
                if (IsOverUI()) { tracking = false; return; }

                tracking = true;
                dragged = false;
                droppedThisPress = false;
                // 押した時点の状態を覚えておく。STARTボタンを押した指を離した拍子に、
                // 始まったばかりのペアを動かしてしまわないようにするため。
                pressState = gameManager != null ? gameManager.State : GameState.Playing;
                pressTime = Time.unscaledTime;
                pressPosition = position;
                stepAnchor = position;
            }

            if (!tracking) return;

            if (held) HandleDrag(position);
            if (releasedNow)
            {
                bool sameState = gameManager == null || gameManager.State == pressState;
                if (sameState && !dragged && !droppedThisPress
                    && Time.unscaledTime - pressTime <= tapMaxSeconds)
                    HandleTap(pressPosition);
                tracking = false;
            }
        }

        /// <summary>下へなぞった分だけ落とす。大きく払ったときは一番下まで。</summary>
        void HandleDrag(Vector2 position)
        {
            float unit = ScreenUnit;
            if ((position - pressPosition).magnitude > tapMaxDrag * unit) dragged = true;
            if (!IsPlaying) return;

            float fromPress = pressPosition.y - position.y;      // 下へ動かすほど正
            if (!droppedThisPress && fromPress >= flickToDrop * unit)
            {
                controller.HardDrop();
                droppedThisPress = true;
                LastAction = "HardDrop";
                return;
            }
            if (droppedThisPress) return;

            float step = Mathf.Max(dragStep * unit, 1f);
            while (stepAnchor.y - position.y >= step)
            {
                stepAnchor.y -= step;
                controller.StepDown();
                LastAction = "StepDown";
            }
        }

        /// <summary>押した場所に応じて、左移動・右移動・回転を振り分ける。</summary>
        void HandleTap(Vector2 position)
        {
            if (gameManager != null && gameManager.State == GameState.GameOver)
            {
                gameManager.Restart();            // 「Rキー」の代わり。スマホにはキーが無いため
                LastAction = "Restart";
                return;
            }
            if (!IsPlaying) return;

            float width = Mathf.Max(Screen.width, 1);
            float x = position.x / width;

            if (x < sideZone) { controller.TryMove(-1); LastAction = "Left"; }
            else if (x > 1f - sideZone) { controller.TryMove(1); LastAction = "Right"; }
            else { controller.TryRotate(true); LastAction = "Rotate"; }
        }

        /// <summary>画面の短い方の長さ。指の動きの基準にする。</summary>
        float ScreenUnit => Mathf.Max(Mathf.Min(Screen.width, Screen.height), 1);

        bool IsPlaying => controller != null
                          && (gameManager == null || gameManager.State == GameState.Playing);

        static bool IsOverUI()
        {
            var es = EventSystem.current;
            return es != null && es.IsPointerOverGameObject();
        }

        /// <summary>マウスと指のどちらでも同じように扱えるよう、押下状態をまとめて読む。</summary>
        bool TryReadPointer(out Vector2 position, out bool pressedNow,
                            out bool held, out bool releasedNow)
        {
            position = default;
            pressedNow = held = releasedNow = false;

#if ENABLE_INPUT_SYSTEM
            // Touchscreen も Pointer を継承しているので、これ1つでマウスと指の両方を拾える
            var pointer = Pointer.current;
            if (pointer == null) return false;

            position = pointer.position.ReadValue();
            pressedNow = pointer.press.wasPressedThisFrame;
            held = pointer.press.isPressed;
            releasedNow = pointer.press.wasReleasedThisFrame;
            return true;
#elif ENABLE_LEGACY_INPUT_MANAGER
            if (Input.touchCount > 0)
            {
                var t = Input.GetTouch(0);
                position = t.position;
                pressedNow = t.phase == TouchPhase.Began;
                held = t.phase != TouchPhase.Ended && t.phase != TouchPhase.Canceled;
                releasedNow = t.phase == TouchPhase.Ended || t.phase == TouchPhase.Canceled;
                return true;
            }
            position = Input.mousePosition;
            pressedNow = Input.GetMouseButtonDown(0);
            held = Input.GetMouseButton(0);
            releasedNow = Input.GetMouseButtonUp(0);
            return true;
#else
            return false;
#endif
        }
    }
}
