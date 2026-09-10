using UnityEngine;

namespace PuyoGame
{
    /// <summary>
    /// 画面下部の操作ボタンと、盤面の操作をつなぐ。
    /// 押しっぱなしのときは、キーボードと同じように少し待ってから連続で効く。
    /// プレイ中だけ表示し、タイトルとゲームオーバーでは隠す。
    /// </summary>
    [DisallowMultipleComponent]
    public class TouchControlPad : MonoBehaviour
    {
        [Header("参照")]
        [SerializeField] PairFallController controller;
        [SerializeField] GameManager gameManager;
        [Tooltip("表示・非表示を切り替える対象。未指定ならこのオブジェクト")]
        [SerializeField] GameObject root;

        [Header("ボタン")]
        [SerializeField] HoldButton moveLeft;
        [SerializeField] HoldButton moveRight;
        [SerializeField] HoldButton rotateLeft;
        [SerializeField] HoldButton rotateRight;
        [SerializeField] HoldButton softDrop;

        [Header("押しっぱなしの効き方")]
        [Tooltip("連続で効き始めるまでの秒数")]
        [Min(0f)]
        [SerializeField] float repeatDelay = 0.22f;
        [Tooltip("連続で効くときの間隔（秒）")]
        [Min(0.02f)]
        [SerializeField] float repeatInterval = 0.09f;

        float leftTimer, rightTimer;
        bool leftRepeating, rightRepeating;

        void Awake()
        {
            if (root == null) root = gameObject;
        }

        void OnEnable()
        {
            // 回転は押しっぱなしで回り続けると扱いにくいので、押した瞬間だけ
            if (rotateLeft != null) rotateLeft.Pressed += RotateCounterClockwise;
            if (rotateRight != null) rotateRight.Pressed += RotateClockwise;
            if (moveLeft != null) moveLeft.Pressed += MoveLeftOnce;
            if (moveRight != null) moveRight.Pressed += MoveRightOnce;
        }

        void OnDisable()
        {
            if (rotateLeft != null) rotateLeft.Pressed -= RotateCounterClockwise;
            if (rotateRight != null) rotateRight.Pressed -= RotateClockwise;
            if (moveLeft != null) moveLeft.Pressed -= MoveLeftOnce;
            if (moveRight != null) moveRight.Pressed -= MoveRightOnce;

            if (controller != null) controller.SoftDropRequested = false;
        }

        void Update()
        {
            bool playing = gameManager == null || gameManager.State == GameState.Playing;
            if (root != null && root.activeSelf != playing) root.SetActive(playing);
            if (!playing || controller == null)
            {
                if (controller != null) controller.SoftDropRequested = false;
                return;
            }

            float dt = Time.deltaTime;
            Repeat(moveLeft, -1, ref leftTimer, ref leftRepeating, dt);
            Repeat(moveRight, 1, ref rightTimer, ref rightRepeating, dt);

            controller.SoftDropRequested = softDrop != null && softDrop.IsHeld;
        }

        /// <summary>押しっぱなしの間、溜め時間の後に一定間隔で動かす。</summary>
        void Repeat(HoldButton button, int dir, ref float timer, ref bool repeating, float dt)
        {
            if (button == null || !button.IsHeld)
            {
                timer = 0f;
                repeating = false;
                return;
            }

            timer += dt;
            float threshold = repeating ? repeatInterval : repeatDelay;
            if (timer < threshold) return;

            timer = 0f;
            repeating = true;
            controller.TryMove(dir);
        }

        // 押した瞬間の1回分。これが無いと、溜め時間ぶん反応が遅れて感じられる。
        void MoveLeftOnce() { if (CanOperate) controller.TryMove(-1); }
        void MoveRightOnce() { if (CanOperate) controller.TryMove(1); }
        void RotateClockwise() { if (CanOperate) controller.TryRotate(true); }
        void RotateCounterClockwise() { if (CanOperate) controller.TryRotate(false); }

        bool CanOperate => controller != null
                           && (gameManager == null || gameManager.State == GameState.Playing);
    }
}
