using UnityEngine;

namespace PuyoGame
{
    /// <summary>
    /// BoardData の内容を SpriteRenderer で画面に表示するだけのクラス。
    /// 落下や操作のロジックは持たない。
    /// ExecuteAlways を付けているので Play を押さなくてもエディタ上で表示を確認できる。
    /// </summary>
    [ExecuteAlways]
    [DisallowMultipleComponent]
    public class BoardView : MonoBehaviour
    {
        [Header("盤面サイズ")]
        [SerializeField] int width = BoardData.DefaultWidth;
        [SerializeField] int height = BoardData.DefaultHeight;

        [Header("表示設定")]
        [Tooltip("1マスのワールド単位でのサイズ")]
        [SerializeField] float cellSize = 1f;
        [Tooltip("マスに対するブロックの大きさの比率")]
        [Range(0.1f, 1f)]
        [SerializeField] float blockScale = 0.9f;
        [Tooltip("未指定なら白い正方形スプライトを実行時に生成する")]
        [SerializeField] Sprite blockSprite;

        [Header("ブロックの見た目")]
        [Tooltip("種類ごとの写真スプライト。未設定の種類は単色の四角で描く")]
        [SerializeField] BlockSprite[] blockSprites;

        [Header("グリッド背景")]
        [SerializeField] bool showGridBackground = true;
        [Tooltip("市松の明暗差は控えめにして、ブロックを見やすくする")]
        [SerializeField] Color gridColorA = new Color(0.19f, 0.20f, 0.27f, 0.80f);
        [SerializeField] Color gridColorB = new Color(0.22f, 0.23f, 0.31f, 0.80f);

        [Header("やわらかい動き（スクワッシュ＆ストレッチ）")]
        [Tooltip("移動・回転・着地・接触にあわせてぷるんと変形させる")]
        [SerializeField] bool squashAndStretch = true;
        [Tooltip("左右移動時の変形量（x=横の伸縮, y=縦の伸縮）")]
        [SerializeField] Vector2 moveSquash = new Vector2(-0.18f, 0.14f);
        [Min(0.02f)]
        [SerializeField] float moveSquashDuration = 0.10f;
        [Tooltip("回転時の変形量")]
        [SerializeField] Vector2 rotateSquash = new Vector2(0.16f, -0.13f);
        [Min(0.02f)]
        [SerializeField] float rotateSquashDuration = 0.12f;
        [Tooltip("着地時の変形量。縦に潰れて横に広がる")]
        [SerializeField] Vector2 landSquash = new Vector2(0.22f, -0.26f);
        [Min(0.02f)]
        [SerializeField] float landSquashDuration = 0.18f;
        [Tooltip("同色がくっついたときのふるえの大きさ")]
        [Range(0f, 0.3f)]
        [SerializeField] float contactWobble = 0.09f;
        [Min(0.02f)]
        [SerializeField] float contactWobbleDuration = 0.28f;
        [Tooltip("ふるえの往復回数")]
        [Min(1f)]
        [SerializeField] float contactWobbleCycles = 3f;

        [Header("落下中ブロックの影")]
        [Tooltip("盤面から浮いて見えるように、落下中のブロックへ影を落とす")]
        [SerializeField] bool activeBlockShadow = true;
        [SerializeField] Vector2 activeShadowOffset = new Vector2(0.07f, -0.09f);
        [SerializeField] Color activeShadowColor = new Color(0f, 0f, 0f, 0.42f);

        [Header("動作確認用")]
        [Tooltip("エディタ上での表示確認用にブロックを1つ置く。Play中は落下処理に任せるので無視される")]
        [SerializeField] bool spawnDemoBlock = true;
        [SerializeField] int demoX = 2;
        [SerializeField] int demoY = 0;
        [SerializeField] PuyoColor demoColor = PuyoColor.Photo1;
        [Tooltip("Play時にメインカメラを盤面が収まる位置・サイズに合わせる")]
        [SerializeField] bool fitCameraOnPlay = true;

        /// <summary>ブロックの種類と、それに使うスプライトの対応。</summary>
        [System.Serializable]
        public class BlockSprite
        {
            public PuyoColor color;
            public Sprite sprite;
        }

        /// <summary>盤面データ本体。</summary>
        public BoardData Board { get; private set; }

        public int Width => width;
        public int Height => height;
        public float CellSize => cellSize;

        SpriteRenderer[,] blockRenderers;
        readonly System.Collections.Generic.Dictionary<PuyoColor, Sprite> spriteLookup
            = new System.Collections.Generic.Dictionary<PuyoColor, Sprite>();
        // スプライトごとにマテリアルを分ける。
        // 1つのマテリアルを共有すると、どのブロックも同じテクスチャで描かれてしまう。
        readonly System.Collections.Generic.Dictionary<Sprite, Material> materialCache
            = new System.Collections.Generic.Dictionary<Sprite, Material>();
        Material baseMaterial;
        SpriteRenderer[] activeBlockRenderers;
        SpriteRenderer[] activeShadowRenderers;

        /// <summary>変形演出の進行状況。</summary>
        class Deform
        {
            public SpriteRenderer Target;
            public Vector2 Amount;
            public float Elapsed;
            public float Duration;
            public bool Wobble;        // true ならふるえ、false なら弾んで戻る
            public float Cycles;
        }
        readonly System.Collections.Generic.List<Deform> deforms
            = new System.Collections.Generic.List<Deform>();

        // 変形は「本来の大きさ」に掛けて使うので、素の値を控えておく
        readonly System.Collections.Generic.Dictionary<SpriteRenderer, Vector3> baseScales
            = new System.Collections.Generic.Dictionary<SpriteRenderer, Vector3>();
        Transform viewRoot;
        Sprite runtimeSprite;
        Texture2D runtimeTexture;

        void OnEnable()
        {
            Rebuild();
        }

        void OnDisable()
        {
            Teardown();
        }

        void Update()
        {
            TickAnimations(Time.deltaTime);
        }

        /// <summary>
        /// 盤面データと表示用オブジェクトを作り直す。Inspector の値を変えた時にも呼ばれる。
        /// </summary>
        public void Rebuild()
        {
            Teardown();

            width = Mathf.Max(1, width);
            height = Mathf.Max(1, height);
            cellSize = Mathf.Max(0.01f, cellSize);

            Board = new BoardData(width, height);
            EnsureSprite();

            // 表示用オブジェクトは毎回生成し直すので、シーンには保存させない
            var rootObject = new GameObject("BoardView (generated)");
            rootObject.hideFlags = HideFlags.DontSave;
            viewRoot = rootObject.transform;
            viewRoot.SetParent(transform, false);

            BuildGridBackground();
            BuildBlockRenderers();
            BuildActiveBlockRenderer();

            // Play 中は落下処理側が盤面を組み立てるので、デモブロックはエディタ表示のときだけ置く
            if (spawnDemoBlock && !Application.isPlaying)
            {
                Board.Set(demoX, demoY, demoColor);
            }

            if (fitCameraOnPlay && Application.isPlaying)
            {
                FitCamera();
            }

            Refresh();
        }

        /// <summary>BoardData の内容を表示に反映する。</summary>
        public void Refresh()
        {
            if (blockRenderers == null || Board == null) return;

            for (int x = 0; x < Board.Width; x++)
            {
                for (int y = 0; y < Board.Height; y++)
                {
                    var color = Board.Get(x, y);
                    var block = blockRenderers[x, y];
                    if (block == null) continue;

                    if (color == PuyoColor.None)
                    {
                        block.enabled = false;
                    }
                    else
                    {
                        block.enabled = true;
                        ApplyBlockAppearance(block, color);
                    }
                }
            }
        }

        /// <summary>同時に表示できる落下中ブロックの数。</summary>
        public const int MaxActiveBlocks = 4;

        /// <summary>
        /// 落下中ブロックを指定マスに表示する。盤面データ（固定済みブロック）は変更しない。
        /// index は 0 から MaxActiveBlocks-1 まで。
        /// </summary>
        public void SetActiveBlock(int index, int x, int y, PuyoColor color)
        {
            if (activeBlockRenderers == null) return;
            if (index < 0 || index >= activeBlockRenderers.Length) return;

            var r = activeBlockRenderers[index];
            if (r == null) return;

            var shadow = (activeShadowRenderers != null && index < activeShadowRenderers.Length)
                ? activeShadowRenderers[index] : null;

            if (color == PuyoColor.None || !IsInside(x, y))
            {
                r.enabled = false;
                if (shadow != null) shadow.enabled = false;
                return;
            }

            r.enabled = true;
            ApplyBlockAppearance(r, color);
            r.transform.position = CellToWorld(x, y);

            // 影は同じ形のまま黒く塗り、少しずらして落とす
            if (shadow != null)
            {
                if (activeBlockShadow)
                {
                    shadow.enabled = true;
                    ApplyBlockAppearance(shadow, color);
                    shadow.color = activeShadowColor;
                    shadow.transform.position = CellToWorld(x, y)
                        + new Vector3(activeShadowOffset.x * cellSize, activeShadowOffset.y * cellSize, 0f);
                }
                else
                {
                    shadow.enabled = false;
                }
            }
        }

        /// <summary>落下中ブロックの表示をすべて消す。</summary>
        public void ClearActiveBlocks()
        {
            if (activeBlockRenderers != null)
                foreach (var r in activeBlockRenderers)
                    if (r != null) r.enabled = false;

            if (activeShadowRenderers != null)
                foreach (var r in activeShadowRenderers)
                    if (r != null) r.enabled = false;
        }

        /// <summary>指定座標が盤面の内側かどうか。</summary>
        public bool IsInside(int x, int y)
        {
            return x >= 0 && x < width && y >= 0 && y < height;
        }

        /// <summary>
        /// ブロック1つの見た目を整える。写真スプライトがあればそれを使い、
        /// 無ければ従来どおり白い四角を色付けして描く。
        /// </summary>
        void ApplyBlockAppearance(SpriteRenderer target, PuyoColor color)
        {
            var sprite = SpriteFor(color);
            if (sprite != null)
            {
                target.sprite = sprite;
                target.sharedMaterial = MaterialFor(sprite);
                target.color = Color.white;

                // スプライトごとに実寸が違うので、1マスに収まる倍率を都度計算する
                float native = sprite.bounds.size.x;
                if (native <= 0f) native = 1f;
                var scale = Vector3.one * (cellSize * blockScale / native);
                target.transform.localScale = scale;
                baseScales[target] = scale;
            }
            else
            {
                target.sprite = blockSprite;
                target.sharedMaterial = MaterialFor(blockSprite);
                target.color = FallbackColor(color);
                var scale = Vector3.one * cellSize * blockScale;
                target.transform.localScale = scale;
                baseScales[target] = scale;
            }
        }

        /// <summary>種類に対応する写真スプライトを返す。消去演出などから使う。</summary>
        public Sprite BlockSpriteFor(PuyoColor color) => SpriteFor(color);

        /// <summary>種類に対応する写真スプライトを返す。無ければ null。</summary>
        Sprite SpriteFor(PuyoColor color)
        {
            if (spriteLookup.Count == 0 && blockSprites != null)
            {
                foreach (var entry in blockSprites)
                {
                    if (entry == null || entry.sprite == null) continue;
                    spriteLookup[entry.color] = entry.sprite;
                }
            }
            return spriteLookup.TryGetValue(color, out var s) ? s : null;
        }

        /// <summary>着地したマスを、縦に潰してから弾ませる。</summary>
        public void PlayLandingSquash(int x, int y)
        {
            if (!squashAndStretch || blockRenderers == null || !IsInside(x, y)) return;
            AddDeform(blockRenderers[x, y], landSquash, landSquashDuration, false, 0f);
        }

        /// <summary>落下中のペアを、左右移動にあわせて横に潰す。</summary>
        public void PlayActiveMoveSquash()
        {
            PlayActiveDeform(moveSquash, moveSquashDuration);
        }

        /// <summary>落下中のペアを、回転の勢いにあわせて変形させる。</summary>
        public void PlayActiveRotateSquash()
        {
            PlayActiveDeform(rotateSquash, rotateSquashDuration);
        }

        void PlayActiveDeform(Vector2 amount, float duration)
        {
            if (!squashAndStretch || activeBlockRenderers == null) return;

            for (int i = 0; i < activeBlockRenderers.Length; i++)
            {
                var block = activeBlockRenderers[i];
                if (block != null && block.enabled) AddDeform(block, amount, duration, false, 0f);

                // 影も同じように変形させないと、形がずれて見える
                if (activeShadowRenderers != null && i < activeShadowRenderers.Length)
                {
                    var shadow = activeShadowRenderers[i];
                    if (shadow != null && shadow.enabled) AddDeform(shadow, amount, duration, false, 0f);
                }
            }
        }

        /// <summary>同色がくっついたマスを、小さくふるわせる。</summary>
        public void PlayContactWobble(int x, int y)
        {
            if (!squashAndStretch || blockRenderers == null || !IsInside(x, y)) return;
            AddDeform(blockRenderers[x, y], new Vector2(contactWobble, -contactWobble),
                      contactWobbleDuration, true, contactWobbleCycles);
        }

        void AddDeform(SpriteRenderer target, Vector2 amount, float duration, bool wobble, float cycles)
        {
            if (target == null || duration <= 0f) return;

            // 同じ対象に重ねない。あとから来た動きを優先する。
            for (int i = deforms.Count - 1; i >= 0; i--)
                if (deforms[i].Target == target) deforms.RemoveAt(i);

            if (!baseScales.ContainsKey(target)) baseScales[target] = target.transform.localScale;

            deforms.Add(new Deform
            {
                Target = target,
                Amount = amount,
                Elapsed = 0f,
                Duration = duration,
                Wobble = wobble,
                Cycles = cycles,
            });
        }

        /// <summary>演出の時間を進める。Update から呼ばれるが、テストからも直接呼べる。</summary>
        public void TickAnimations(float dt)
        {
            if (deforms.Count == 0) return;

            for (int i = deforms.Count - 1; i >= 0; i--)
            {
                var d = deforms[i];
                if (d.Target == null) { deforms.RemoveAt(i); continue; }

                var baseScale = baseScales.TryGetValue(d.Target, out var b)
                    ? b : d.Target.transform.localScale;

                d.Elapsed += dt;
                if (d.Elapsed >= d.Duration)
                {
                    d.Target.transform.localScale = baseScale;
                    deforms.RemoveAt(i);
                    continue;
                }

                float p = d.Elapsed / d.Duration;
                float k = d.Wobble
                    ? Mathf.Sin(p * Mathf.PI * 2f * d.Cycles) * (1f - p)   // 減衰しながら往復
                    : 1f - EaseOutBack(p);                                 // 弾んで戻る

                d.Target.transform.localScale = new Vector3(
                    baseScale.x * (1f + d.Amount.x * k),
                    baseScale.y * (1f + d.Amount.y * k),
                    baseScale.z);
            }
        }

        /// <summary>行き過ぎてから戻る曲線。ゴムのような弾力を出す。</summary>
        static float EaseOutBack(float p)
        {
            const float c1 = 1.70158f;
            const float c3 = c1 + 1f;
            float u = p - 1f;
            return 1f + c3 * u * u * u + c1 * u * u;
        }

        /// <summary>再生中の変形演出の数（動作確認用）。</summary>
        public int ActiveDeformCount => deforms.Count;

        /// <summary>マス座標をワールド座標（マスの中心）に変換する。</summary>
        public Vector3 CellToWorld(int x, int y)
        {
            float originX = -width * cellSize * 0.5f;
            float originY = -height * cellSize * 0.5f;
            return transform.position + new Vector3(
                originX + (x + 0.5f) * cellSize,
                originY + (y + 0.5f) * cellSize,
                0f);
        }

        /// <summary>盤面全体がちょうど収まる直交カメラのサイズを返す。</summary>
        public float GetRequiredOrthographicSize(float aspect, float margin = 1.1f)
        {
            if (aspect <= 0f) aspect = 1f;
            float halfHeight = height * cellSize * 0.5f;
            float halfWidth = width * cellSize * 0.5f;
            return Mathf.Max(halfHeight, halfWidth / aspect) * margin;
        }

        void Teardown()
        {
            if (viewRoot != null)
            {
                DestroySafely(viewRoot.gameObject);
                viewRoot = null;
            }
            else
            {
                // ドメインリロードなどで参照が切れた場合の取り残しを掃除する
                for (int i = transform.childCount - 1; i >= 0; i--)
                {
                    var child = transform.GetChild(i);
                    if (child.name == "BoardView (generated)")
                    {
                        DestroySafely(child.gameObject);
                    }
                }
            }

            deforms.Clear();
            baseScales.Clear();
            blockRenderers = null;
            activeBlockRenderers = null;
            activeShadowRenderers = null;
            spriteLookup.Clear();

            foreach (var m in materialCache.Values) DestroySafely(m);
            materialCache.Clear();
            baseMaterial = null;

            if (runtimeSprite != null)
            {
                if (blockSprite == runtimeSprite) blockSprite = null;
                DestroySafely(runtimeSprite);
                runtimeSprite = null;
            }
            if (runtimeTexture != null)
            {
                DestroySafely(runtimeTexture);
                runtimeTexture = null;
            }
        }

        static void DestroySafely(Object target)
        {
            if (target == null) return;
            if (Application.isPlaying) Destroy(target);
            else DestroyImmediate(target);
        }

        void EnsureSprite()
        {
            if (blockSprite != null) return;

            // スプライト素材を用意しなくても表示確認できるよう、白い正方形スプライトを生成する
            runtimeTexture = new Texture2D(1, 1, TextureFormat.RGBA32, false)
            {
                filterMode = FilterMode.Point,
                name = "PuyoGame_WhitePixel",
                hideFlags = HideFlags.DontSave,
            };
            runtimeTexture.SetPixel(0, 0, Color.white);
            runtimeTexture.Apply();

            // meshType は FullRect にする（Tight だと小さなテクスチャでメッシュが作られないことがある）
            runtimeSprite = Sprite.Create(
                runtimeTexture,
                new Rect(0f, 0f, 1f, 1f),
                new Vector2(0.5f, 0.5f),
                1f,
                0,
                SpriteMeshType.FullRect);
            runtimeSprite.name = "PuyoGame_Square";
            runtimeSprite.hideFlags = HideFlags.DontSave;
            blockSprite = runtimeSprite;
        }

        void BuildGridBackground()
        {
            if (!showGridBackground) return;

            var root = CreateChild("GridBackground", viewRoot);
            for (int x = 0; x < width; x++)
            {
                for (int y = 0; y < height; y++)
                {
                    var cell = CreateSpriteObject($"Cell_{x}_{y}", root);
                    cell.transform.position = CellToWorld(x, y);
                    cell.transform.localScale = Vector3.one * cellSize;
                    cell.sortingOrder = 0;
                    cell.color = ((x + y) % 2 == 0) ? gridColorA : gridColorB;
                }
            }
        }

        void BuildBlockRenderers()
        {
            var root = CreateChild("Blocks", viewRoot);

            blockRenderers = new SpriteRenderer[width, height];
            for (int x = 0; x < width; x++)
            {
                for (int y = 0; y < height; y++)
                {
                    var block = CreateSpriteObject($"Block_{x}_{y}", root);
                    block.transform.position = CellToWorld(x, y);
                    block.transform.localScale = Vector3.one * cellSize * blockScale;
                    block.sortingOrder = 1;
                    block.enabled = false;
                    blockRenderers[x, y] = block;
                }
            }
        }

        void BuildActiveBlockRenderer()
        {
            // 影 → 落下中ブロック の順に重ねる（どちらも固定済みブロックより手前）
            activeShadowRenderers = new SpriteRenderer[MaxActiveBlocks];
            activeBlockRenderers = new SpriteRenderer[MaxActiveBlocks];
            for (int i = 0; i < MaxActiveBlocks; i++)
            {
                var shadow = CreateSpriteObject($"ActiveShadow_{i}", viewRoot);
                shadow.transform.localScale = Vector3.one * cellSize * blockScale;
                shadow.sortingOrder = 2;
                shadow.enabled = false;
                activeShadowRenderers[i] = shadow;

                var r = CreateSpriteObject($"ActiveBlock_{i}", viewRoot);
                r.transform.localScale = Vector3.one * cellSize * blockScale;
                r.sortingOrder = 3;
                r.enabled = false;
                activeBlockRenderers[i] = r;
            }
        }

        static Transform CreateChild(string name, Transform parent)
        {
            var go = new GameObject(name);
            go.hideFlags = HideFlags.DontSave;
            go.transform.SetParent(parent, false);
            return go.transform;
        }

        SpriteRenderer CreateSpriteObject(string name, Transform parent)
        {
            var go = new GameObject(name);
            go.hideFlags = HideFlags.DontSave;
            go.transform.SetParent(parent, false);
            var spriteRenderer = go.AddComponent<SpriteRenderer>();

            // 最初の1つから既定マテリアルを控えておき、以降はこれを複製して使う
            if (baseMaterial == null) baseMaterial = spriteRenderer.sharedMaterial;

            spriteRenderer.sprite = blockSprite;
            spriteRenderer.sharedMaterial = MaterialFor(blockSprite);
            return spriteRenderer;
        }

        /// <summary>
        /// スプライト専用のマテリアルを返す。
        /// SpriteRenderer 同士でマテリアルを共有すると、最後に設定されたテクスチャが
        /// 全体に描かれてしまうため、テクスチャごとに分ける。
        /// </summary>
        Material MaterialFor(Sprite sprite)
        {
            if (sprite == null) return baseMaterial;
            if (materialCache.TryGetValue(sprite, out var cached) && cached != null) return cached;
            if (baseMaterial == null) return null;

            var created = new Material(baseMaterial)
            {
                name = $"PuyoGame_{sprite.name}",
                hideFlags = HideFlags.DontSave,
            };
            materialCache[sprite] = created;
            return created;
        }

        void FitCamera()
        {
            var cam = Camera.main;
            if (cam == null || !cam.orthographic) return;

            cam.transform.position = new Vector3(
                transform.position.x,
                transform.position.y,
                cam.transform.position.z);
            cam.orthographicSize = GetRequiredOrthographicSize(cam.aspect);
        }

        /// <summary>写真スプライトが無い場合に使う代替色。</summary>
        static Color FallbackColor(PuyoColor color)
        {
            switch (color)
            {
                case PuyoColor.Photo1: return new Color(0.92f, 0.26f, 0.26f);
                case PuyoColor.Photo2: return new Color(0.30f, 0.78f, 0.36f);
                case PuyoColor.Photo3: return new Color(0.26f, 0.51f, 0.93f);
                default: return Color.clear;
            }
        }

#if UNITY_EDITOR
        void OnValidate()
        {
            // OnValidate 中は GameObject を生成・破棄できないので次のタイミングに回す
            UnityEditor.EditorApplication.delayCall += RebuildFromInspector;
        }

        void RebuildFromInspector()
        {
            UnityEditor.EditorApplication.delayCall -= RebuildFromInspector;
            if (this == null || !isActiveAndEnabled) return;
            Rebuild();
        }

        // シーンビューで盤面の範囲を確認できるようにする
        void OnDrawGizmos()
        {
            Gizmos.color = new Color(0f, 1f, 1f, 0.5f);
            Gizmos.DrawWireCube(
                transform.position,
                new Vector3(width * cellSize, height * cellSize, 0f));
        }
#endif
    }
}
