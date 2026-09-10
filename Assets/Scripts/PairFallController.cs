using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Serialization;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace PuyoGame
{
    /// <summary>子ブロックが軸ブロックから見てどの向きにあるか。</summary>
    public enum PairRotation
    {
        Up = 0,
        Right = 1,
        Down = 2,
        Left = 3,
    }

    /// <summary>
    /// 2個1組のペアを落下させ、移動・回転・着地を扱うクラス。
    /// マッチ判定・消去・連鎖はまだ扱わない。
    /// </summary>
    [RequireComponent(typeof(BoardView))]
    [DisallowMultipleComponent]
    public class PairFallController : MonoBehaviour
    {
        /// <summary>1マス落ちるまでの秒数の既定値。</summary>
        public const float DefaultFallInterval = 0.75f;

        [Header("落下")]
        [Tooltip("1マス落ちるまでの秒数")]
        [Min(0.01f)]
        [SerializeField] float fallInterval = DefaultFallInterval;
        [Tooltip("下キーを押している間、落下を何倍速にするか")]
        [Min(1f)]
        [SerializeField] float softDropMultiplier = 10f;

        [Header("左右移動")]
        [Tooltip("押しっぱなしで連続移動が始まるまでの秒数")]
        [Min(0f)]
        [SerializeField] float moveRepeatDelay = 0.18f;
        [Tooltip("連続移動の間隔（秒）")]
        [Min(0.01f)]
        [SerializeField] float moveRepeatInterval = 0.06f;

        [Header("出現")]
        [Tooltip("色を毎回ランダムにする。オフなら下の固定色を使う")]
        [SerializeField] bool randomizeColor = true;
        [SerializeField] PuyoColor fixedAxisColor = PuyoColor.Photo1;
        [SerializeField] PuyoColor fixedChildColor = PuyoColor.Photo3;
        [Tooltip("着地して固定された後に次のペアを出す")]
        [SerializeField] bool spawnNextAfterLock = true;
        [Tooltip("Start時に自動でペアを出す。GameManager が開始を制御する場合はオフにする")]
        [SerializeField] bool autoStartOnPlay = true;

        [Header("消去")]
        [Tooltip("同色が縦か横に一直線でこの数以上連続すると消える")]
        [Min(2)]
        [FormerlySerializedAs("minGroupSize")]
        [SerializeField] int minLineLength = BoardResolver.DefaultMinLineLength;
        [Tooltip("消去のたびに連鎖数と消去マス数をコンソールに出す（動作確認用）")]
        [SerializeField] bool logClears = false;

        [Header("演出")]
        [Tooltip("消去時のキラキラ演出。未設定でもゲームは動く")]
        [SerializeField] ClearEffectPlayer effectPlayer;
        [Tooltip("効果音。未設定でも無音で動く")]
        [SerializeField] GameAudio gameAudio;

        BoardView view;
        readonly List<int> clearedPerChain = new List<int>();
        Action<int, IReadOnlyList<ClearedCell>> stepClearedCallback;
        float fallTimer;
        float moveTimer;
        int lastMoveDir;
        bool moveRepeating;

        /// <summary>落下中のペアがあるか。</summary>
        public bool HasActivePair { get; private set; }
        /// <summary>出現位置が塞がっていて、もう置けなくなった状態。</summary>
        public bool IsGameOver { get; private set; }
        /// <summary>直前の着地で成立した連鎖数（消えなければ0）。</summary>
        public int LastChainCount { get; private set; }

        /// <summary>
        /// 着地後の消去が1回でも起きたときに発火する。
        /// 引数は各連鎖ステップで消えたマス数（index0 が1連鎖目）。
        /// </summary>
        public event Action<IReadOnlyList<int>> ChainResolved;

        /// <summary>出現位置が塞がってゲームオーバーになったときに発火する。</summary>
        public event Action GameOverOccurred;

        public int AxisX { get; private set; }
        public int AxisY { get; private set; }
        public PairRotation Rotation { get; private set; }
        public PuyoColor AxisColor { get; private set; }
        public PuyoColor ChildColor { get; private set; }

        /// <summary>子ブロックのX座標（軸からの相対位置で決まる）。</summary>
        public int ChildX => AxisX + DirX(Rotation);
        /// <summary>子ブロックのY座標。</summary>
        public int ChildY => AxisY + DirY(Rotation);

        BoardData Board => EnsureView() ? view.Board : null;

        void Awake()
        {
            EnsureView();
        }

        bool EnsureView()
        {
            if (view == null) view = GetComponent<BoardView>();
            return view != null;
        }

        void Start()
        {
            // BoardView の OnEnable で盤面が作られた後に開始する
            if (autoStartOnPlay) SpawnPair();
        }

        void Update()
        {
            if (IsGameOver) return;

            float dt = Time.deltaTime;
            HandleHorizontalInput(dt);
            HandleRotateInput();

            if (!HasActivePair) return;

            // 下キーを押している間だけ落下間隔を短くする
            float interval = IsSoftDropHeld() ? fallInterval / softDropMultiplier : fallInterval;
            fallTimer += dt;
            while (fallTimer >= interval)
            {
                fallTimer -= interval;
                StepDown();
                if (!HasActivePair || IsGameOver) break;
            }
        }

        // ---------------- 操作 ----------------

        /// <summary>ペア全体を左右に1マス動かす。塞がっていたら動かさず false を返す。</summary>
        public bool TryMove(int dx)
        {
            if (!HasActivePair || IsGameOver || dx == 0) return false;
            if (!CanPlace(AxisX + dx, AxisY, Rotation)) return false;

            AxisX += dx;
            ApplyToView();
            view.PlayActiveMoveSquash();
            if (gameAudio != null) gameAudio.PlayMove();
            return true;
        }

        /// <summary>
        /// 軸ブロックを中心に90度回転する。回転先が塞がっていたら回転しない（壁蹴りなし）。
        /// </summary>
        public bool TryRotate(bool clockwise = true)
        {
            if (!HasActivePair || IsGameOver) return false;

            var next = (PairRotation)(((int)Rotation + (clockwise ? 1 : 3)) % 4);
            if (!CanPlace(AxisX, AxisY, next)) return false;

            Rotation = next;
            ApplyToView();
            view.PlayActiveRotateSquash();
            if (gameAudio != null) gameAudio.PlayRotate();
            return true;
        }

        /// <summary>
        /// ペアを1マス落とす。どちらか一方でも下に進めなければ、その場で盤面に固定する。
        /// </summary>
        public bool StepDown()
        {
            if (!HasActivePair || IsGameOver) return false;

            if (CanPlace(AxisX, AxisY - 1, Rotation))
            {
                AxisY--;
                ApplyToView();
                return true;
            }

            LockPair();
            return false;
        }

        /// <summary>落下中のペアを、着地するまで一気に落とす。</summary>
        public void HardDrop()
        {
            while (HasActivePair && !IsGameOver && StepDown()) { }
        }

        // ---------------- 出現・固定 ----------------

        /// <summary>新しいペアを盤面上部中央に出す。出せなければゲームオーバー。</summary>
        public void SpawnPair()
        {
            if (!EnsureView() || Board == null)
            {
                Debug.LogError("[PuyoGame] 盤面データがありません。BoardView が有効か確認してください。", this);
                enabled = false;
                return;
            }

            // 軸は最上段。子は盤面の1マス上にはみ出した状態で出て、落ちながら入ってくる。
            // こうすると「最上段が埋まったとき」だけがゲームオーバーになる。
            int x = (Board.Width - 1) / 2;
            int y = Board.Height - 1;
            var rot = PairRotation.Up;

            if (!CanPlace(x, y, rot))
            {
                TriggerGameOver("出現位置（最上段）が埋まった");
                return;
            }

            AxisX = x;
            AxisY = y;
            Rotation = rot;
            AxisColor = randomizeColor ? RandomColor() : fixedAxisColor;
            ChildColor = randomizeColor ? RandomColor() : fixedChildColor;
            HasActivePair = true;
            fallTimer = 0f;

            ApplyToView();
        }

        /// <summary>ペアを2つの独立したブロックとして盤面データに書き込む。</summary>
        void LockPair()
        {
            bool axisPlaced = Board.Set(AxisX, AxisY, AxisColor);
            bool childPlaced = Board.Set(ChildX, ChildY, ChildColor);
            // 盤面の上にはみ出したまま止まった = 最上段まで埋まりきった
            bool overflowed = !axisPlaced || !childPlaced;

            HasActivePair = false;
            view.ClearActiveBlocks();
            view.Refresh();                      // 固定した見た目にしてから演出を始める
            view.PlayLandingSquash(AxisX, AxisY);
            view.PlayLandingSquash(ChildX, ChildY);
            WobbleContacts(AxisX, AxisY);
            WobbleContacts(ChildX, ChildY);
            if (gameAudio != null) gameAudio.PlayLand();

            // 演出が要らないときはコールバックを渡さず、余計なリスト確保を避ける
            if (effectPlayer != null && stepClearedCallback == null)
                stepClearedCallback = OnStepCleared;

            // 浮きブロックの落下・消去・連鎖をすべて終えてから次のペアを出す
            LastChainCount = BoardResolver.Resolve(
                Board, minLineLength, clearedPerChain,
                effectPlayer != null ? stepClearedCallback : null);
            view.Refresh();

            if (logClears && clearedPerChain.Count > 0)
            {
                for (int i = 0; i < clearedPerChain.Count; i++)
                    Debug.Log($"[PuyoGame] {i + 1}連鎖目: {clearedPerChain[i]}マス消去 (必要連続数={minLineLength})", this);
            }

            if (clearedPerChain.Count > 0) ChainResolved?.Invoke(clearedPerChain);

            // はみ出した1個は盤面に入らない。ただし着地で消去が起きて最上段が空いたなら、
            // まだ続けられる。続けるかどうかの判断は SpawnPair 側に一本化する。
            if (overflowed)
                Debug.Log("[PuyoGame] 盤面の外にはみ出した1個は置けませんでした。", this);

            if (spawnNextAfterLock) SpawnPair();
        }

        /// <summary>ゲームオーバーにして通知する。</summary>
        void TriggerGameOver(string reason)
        {
            HasActivePair = false;
            IsGameOver = true;
            view.ClearActiveBlocks();
            Debug.Log($"[PuyoGame] ゲームオーバー: {reason}", this);
            GameOverOccurred?.Invoke();
        }

        static readonly int[] NeighborX = { 1, -1, 0, 0 };
        static readonly int[] NeighborY = { 0, 0, 1, -1 };

        /// <summary>
        /// 置いたマスの上下左右に同じ色があれば、両方をふるわせる。
        /// くっついた手応えを出すための演出。
        /// </summary>
        void WobbleContacts(int x, int y)
        {
            var color = Board.Get(x, y);
            if (color == PuyoColor.None) return;

            bool touched = false;
            for (int i = 0; i < NeighborX.Length; i++)
            {
                int nx = x + NeighborX[i];
                int ny = y + NeighborY[i];
                if (Board.Get(nx, ny) != color) continue;

                view.PlayContactWobble(nx, ny);
                touched = true;
            }
            if (touched) view.PlayContactWobble(x, y);
        }

        /// <summary>消去1段ごとに呼ばれ、その段で消えたマスに演出を出す。</summary>
        void OnStepCleared(int chain, IReadOnlyList<ClearedCell> cells)
        {
            effectPlayer.Play(chain, cells);
        }

        /// <summary>
        /// 盤面を空にしてゲームオーバーを解除する。ペアは出さない（タイトル表示用）。
        /// </summary>
        public void ClearBoard()
        {
            if (!EnsureView() || Board == null) return;

            Board.Clear();
            HasActivePair = false;
            IsGameOver = false;
            LastChainCount = 0;
            clearedPerChain.Clear();

            fallTimer = 0f;
            moveTimer = 0f;
            lastMoveDir = 0;
            moveRepeating = false;

            view.ClearActiveBlocks();
            view.Refresh();

            if (effectPlayer != null) effectPlayer.StopAll();
        }

        /// <summary>盤面を空にして最初から再開する。</summary>
        public void Restart()
        {
            ClearBoard();
            SpawnPair();
        }

        /// <summary>指定の軸位置・回転でペアを置けるか。盤外と埋まっているマスは不可。</summary>
        public bool CanPlace(int axisX, int axisY, PairRotation rot)
        {
            if (Board == null) return false;
            int cx = axisX + DirX(rot);
            int cy = axisY + DirY(rot);
            return IsFreeForActivePair(axisX, axisY) && IsFreeForActivePair(cx, cy);
        }

        /// <summary>
        /// 落下中のペアがそのマスに居られるか。
        /// 盤面のすぐ上の1段だけは、出現直後に子ブロックがはみ出すため空き扱いにする。
        /// 固定するときは Board.Set が弾くので、はみ出したまま盤面に書き込まれることはない。
        /// </summary>
        bool IsFreeForActivePair(int x, int y)
        {
            if (Board == null) return false;
            if (x < 0 || x >= Board.Width || y < 0) return false;
            if (y >= Board.Height) return y == Board.Height;
            return Board.IsEmpty(x, y);
        }

        void ApplyToView()
        {
            view.SetActiveBlock(0, AxisX, AxisY, AxisColor);
            view.SetActiveBlock(1, ChildX, ChildY, ChildColor);
        }

        static int DirX(PairRotation r) => r == PairRotation.Right ? 1 : r == PairRotation.Left ? -1 : 0;
        static int DirY(PairRotation r) => r == PairRotation.Up ? 1 : r == PairRotation.Down ? -1 : 0;

        static PuyoColor RandomColor()
        {
            int count = System.Enum.GetValues(typeof(PuyoColor)).Length - 1;   // None を除く
            return (PuyoColor)UnityEngine.Random.Range(1, count + 1);
        }

        // ---------------- 入力 ----------------

        void HandleHorizontalInput(float dt)
        {
            int dir = 0;
            if (IsLeftHeld()) dir -= 1;
            if (IsRightHeld()) dir += 1;

            if (dir == 0)
            {
                lastMoveDir = 0;
                moveTimer = 0f;
                moveRepeating = false;
                return;
            }

            if (dir != lastMoveDir)
            {
                // 押した瞬間はすぐ1マス動かす
                TryMove(dir);
                lastMoveDir = dir;
                moveTimer = 0f;
                moveRepeating = false;
                return;
            }

            // 押しっぱなしなら、溜め時間の後に一定間隔で動かす
            moveTimer += dt;
            float threshold = moveRepeating ? moveRepeatInterval : moveRepeatDelay;
            if (moveTimer >= threshold)
            {
                moveTimer = 0f;
                moveRepeating = true;
                TryMove(dir);
            }
        }

        void HandleRotateInput()
        {
            if (IsRotatePressed()) TryRotate(true);
        }

#if ENABLE_INPUT_SYSTEM
        static Keyboard Kb => Keyboard.current;
        bool IsLeftHeld() => Kb != null && Kb.leftArrowKey.isPressed;
        bool IsRightHeld() => Kb != null && Kb.rightArrowKey.isPressed;
        bool IsSoftDropHeld() => Kb != null && Kb.downArrowKey.isPressed;
        bool IsRotatePressed() => Kb != null && (Kb.upArrowKey.wasPressedThisFrame || Kb.spaceKey.wasPressedThisFrame);
#elif ENABLE_LEGACY_INPUT_MANAGER
        bool IsLeftHeld() => Input.GetKey(KeyCode.LeftArrow);
        bool IsRightHeld() => Input.GetKey(KeyCode.RightArrow);
        bool IsSoftDropHeld() => Input.GetKey(KeyCode.DownArrow);
        bool IsRotatePressed() => Input.GetKeyDown(KeyCode.UpArrow) || Input.GetKeyDown(KeyCode.Space);
#else
        bool IsLeftHeld() => false;
        bool IsRightHeld() => false;
        bool IsSoftDropHeld() => false;
        bool IsRotatePressed() => false;
#endif
    }
}
