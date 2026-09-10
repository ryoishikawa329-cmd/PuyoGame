using System.Collections.Generic;
using UnityEngine;

namespace PuyoGame
{
    /// <summary>
    /// ブロック消去時のキラキラ演出を再生する。
    /// SpriteRenderer を使い回し、コマ送りでアニメーションさせる。
    /// ゲーム進行は止めず、見た目だけを非同期に流す。
    /// </summary>
    [DisallowMultipleComponent]
    public class ClearEffectPlayer : MonoBehaviour
    {
        /// <summary>色ごとのコマ画像。</summary>
        [System.Serializable]
        public class SparkleSet
        {
            public PuyoColor color;
            public Sprite[] frames;
        }

        /// <summary>再生中エフェクトの情報（動作確認用）。</summary>
        public readonly struct ActiveEffectInfo
        {
            public readonly Vector3 Position;
            public readonly PuyoColor Color;
            public readonly bool Visible;

            public ActiveEffectInfo(Vector3 position, PuyoColor color, bool visible)
            {
                Position = position;
                Color = color;
                Visible = visible;
            }
        }

        [Header("参照")]
        [SerializeField] BoardView boardView;
        [Tooltip("色ごとのキラキラ画像（コマ順に並べる）")]
        [SerializeField] SparkleSet[] sparkleSets;

        [Header("再生")]
        [Tooltip("1つのエフェクトを再生し切るまでの秒数")]
        [Min(0.05f)]
        [SerializeField] float duration = 0.4f;
        [Tooltip("連鎖の段ごとに演出をずらす秒数。0 なら全段同時に出る")]
        [Min(0f)]
        [SerializeField] float chainStepDelay = 0.15f;
        [Tooltip("1マスに対するエフェクトの大きさ")]
        [Min(0.1f)]
        [SerializeField] float sizeMultiplier = 1.6f;
        [Tooltip("ブロックより手前に描くための描画順")]
        [SerializeField] int sortingOrder = 5;
        [Tooltip("演出用のマテリアル。2Dライトの影響を受けない Sprites-Default を想定")]
        [SerializeField] Material material;

        [Header("消える動き")]
        [Tooltip("消えるブロックを、ぷるんと膨らませてから揺れながらしぼませる")]
        [SerializeField] bool vanishAnimation = true;
        [Min(0.05f)]
        [SerializeField] float vanishDuration = 0.26f;
        [Tooltip("どれくらい膨らむか（0.30 なら最大30%大きくなる）")]
        [Range(0f, 0.6f)]
        [SerializeField] float vanishPop = 0.30f;
        [Tooltip("しぼむときの左右の揺れ幅")]
        [Range(0f, 0.4f)]
        [SerializeField] float vanishWobble = 0.14f;
        [Min(1f)]
        [SerializeField] float vanishWobbleCycles = 5f;
        [Tooltip("ブロックより手前、キラキラより奥に描く")]
        [SerializeField] int vanishSortingOrder = 4;

        class Instance
        {
            public SpriteRenderer Renderer;
            public Sprite[] Frames;
            public PuyoColor Color;
            public float Elapsed;
            public float Delay;
            public bool Active;
        }

        /// <summary>消えるブロックの進行状況。</summary>
        class Vanish
        {
            public SpriteRenderer Renderer;
            public float Elapsed;
            public float BaseScale;
            public bool Active;
        }

        readonly List<Instance> pool = new List<Instance>();
        readonly List<Vanish> vanishPool = new List<Vanish>();
        readonly Dictionary<PuyoColor, Sprite[]> frameLookup = new Dictionary<PuyoColor, Sprite[]>();
        Transform root;
        Material fallbackMaterial;

        /// <summary>再生中のエフェクト数。</summary>
        public int ActiveCount
        {
            get
            {
                int n = 0;
                foreach (var e in pool) if (e.Active) n++;
                return n;
            }
        }

        void Awake()
        {
            BuildLookup();
        }

        void OnDisable()
        {
            StopAll();
        }

        void OnDestroy()
        {
            if (fallbackMaterial != null)
            {
                if (Application.isPlaying) Destroy(fallbackMaterial);
                else DestroyImmediate(fallbackMaterial);
                fallbackMaterial = null;
            }
        }

        void Update()
        {
            Tick(Time.deltaTime);
        }

        void BuildLookup()
        {
            frameLookup.Clear();
            if (sparkleSets == null) return;
            foreach (var set in sparkleSets)
            {
                if (set == null || set.frames == null || set.frames.Length == 0) continue;
                frameLookup[set.color] = set.frames;
            }
        }

        /// <summary>
        /// 消えたマスに演出を出す。chain は1始まりの連鎖数で、段ごとに開始を少しずらす。
        /// cells は呼び出し側で使い回されるため、この場で読み切る。
        /// </summary>
        public void Play(int chain, IReadOnlyList<ClearedCell> cells)
        {
            if (cells == null || cells.Count == 0) return;
            if (frameLookup.Count == 0) BuildLookup();
            if (boardView == null || frameLookup.Count == 0) return;

            EnsureRoot();
            float delay = Mathf.Max(0, chain - 1) * chainStepDelay;

            foreach (var cell in cells)
            {
                if (!frameLookup.TryGetValue(cell.Color, out var frames)) continue;

                var e = Rent();
                e.Frames = frames;
                e.Color = cell.Color;
                e.Elapsed = 0f;
                e.Delay = delay;
                e.Active = true;

                var tf = e.Renderer.transform;
                tf.position = boardView.CellToWorld(cell.X, cell.Y);
                tf.localScale = Vector3.one * ScaleFor(frames[0]);

                // URP の既定は Sprite-Lit-Default で、2Dライトの影響で透明が正しく出ない。
                // 設定漏れがあっても不透明な四角にならないよう、再生のたびに非ライトを当てる。
                var mat = ResolveMaterial();
                if (mat != null && e.Renderer.sharedMaterial != mat) e.Renderer.sharedMaterial = mat;

                e.Renderer.sprite = frames[0];
                e.Renderer.sortingOrder = sortingOrder;
                e.Renderer.enabled = false;      // 遅延中は隠しておく
            }

            if (vanishAnimation) StartVanish(cells, delay);
        }

        /// <summary>消えるマスに、ブロックの絵をそのまま置いてしぼませる。</summary>
        void StartVanish(IReadOnlyList<ClearedCell> cells, float delay)
        {
            foreach (var cell in cells)
            {
                var sprite = boardView.BlockSpriteFor(cell.Color);
                if (sprite == null) continue;

                var v = RentVanish();
                v.Elapsed = -delay;              // 連鎖の段に合わせて遅らせる
                v.Active = true;

                float native = sprite.bounds.size.x;
                if (native <= 0f) native = 1f;
                v.BaseScale = boardView.CellSize * 0.9f / native;

                v.Renderer.sprite = sprite;
                v.Renderer.sortingOrder = vanishSortingOrder;
                v.Renderer.color = Color.white;
                v.Renderer.transform.position = boardView.CellToWorld(cell.X, cell.Y);
                v.Renderer.transform.localScale = Vector3.one * v.BaseScale;
                v.Renderer.enabled = delay <= 0f;
            }
        }

        Vanish RentVanish()
        {
            foreach (var v in vanishPool) if (!v.Active) return v;

            var go = new GameObject($"Vanish_{vanishPool.Count}");
            go.hideFlags = HideFlags.DontSave;
            go.transform.SetParent(root, false);
            var created = new Vanish { Renderer = go.AddComponent<SpriteRenderer>() };
            var mat = ResolveMaterial();
            if (mat != null) created.Renderer.sharedMaterial = new Material(mat)
            {
                name = "PuyoGame_Vanish",
                hideFlags = HideFlags.DontSave,
            };
            created.Renderer.enabled = false;
            vanishPool.Add(created);
            return created;
        }

        void TickVanish(float dt)
        {
            foreach (var v in vanishPool)
            {
                if (!v.Active) continue;

                v.Elapsed += dt;
                if (v.Elapsed < 0f) { v.Renderer.enabled = false; continue; }
                if (v.Elapsed >= vanishDuration)
                {
                    v.Active = false;
                    v.Renderer.enabled = false;
                    continue;
                }

                float p = v.Elapsed / vanishDuration;

                // 前半でふくらみ、後半でしぼむ
                const float popEnd = 0.28f;
                float size = p < popEnd
                    ? Mathf.Lerp(1f, 1f + vanishPop, EaseOut(p / popEnd))
                    : Mathf.Lerp(1f + vanishPop, 0f, EaseIn((p - popEnd) / (1f - popEnd)));

                // しぼみながら左右にゆれる
                float w = vanishWobble * Mathf.Sin(p * Mathf.PI * 2f * vanishWobbleCycles) * (1f - p);

                v.Renderer.enabled = true;
                v.Renderer.transform.localScale = new Vector3(
                    v.BaseScale * size * (1f + w),
                    v.BaseScale * size * (1f - w),
                    1f);
            }
        }

        static float EaseOut(float p) => 1f - (1f - p) * (1f - p);
        static float EaseIn(float p) => p * p;

        /// <summary>しぼみ中のブロック数（動作確認用）。</summary>
        public int ActiveVanishCount
        {
            get
            {
                int n = 0;
                foreach (var v in vanishPool) if (v.Active) n++;
                return n;
            }
        }

        /// <summary>時間を進める。Update から呼ばれるが、テストからも直接呼べる。</summary>
        public void Tick(float dt)
        {
            TickVanish(dt);

            foreach (var e in pool)
            {
                if (!e.Active) continue;

                e.Elapsed += dt;
                float local = e.Elapsed - e.Delay;

                if (local < 0f)
                {
                    e.Renderer.enabled = false;
                    continue;
                }
                if (local >= duration)
                {
                    Release(e);
                    continue;
                }

                // 経過時間からコマ番号を求めるので、フレームレートに依存しない
                int index = Mathf.Clamp(
                    (int)(local / duration * e.Frames.Length), 0, e.Frames.Length - 1);
                e.Renderer.sprite = e.Frames[index];
                e.Renderer.enabled = true;
            }
        }

        /// <summary>再生中のエフェクトをすべて止める。</summary>
        public void StopAll()
        {
            foreach (var e in pool) if (e.Active) Release(e);
            foreach (var v in vanishPool)
            {
                v.Active = false;
                if (v.Renderer != null) v.Renderer.enabled = false;
            }
        }

        /// <summary>再生中エフェクトの一覧を取り出す（動作確認用）。</summary>
        public void CollectActive(List<ActiveEffectInfo> results)
        {
            results.Clear();
            foreach (var e in pool)
            {
                if (!e.Active) continue;
                results.Add(new ActiveEffectInfo(
                    e.Renderer.transform.position, e.Color, e.Renderer.enabled));
            }
        }

        float ScaleFor(Sprite sprite)
        {
            float native = sprite != null ? sprite.bounds.size.x : 1f;
            if (native <= 0f) native = 1f;
            float cell = boardView != null ? boardView.CellSize : 1f;
            return cell * sizeMultiplier / native;
        }

        void EnsureRoot()
        {
            if (root != null) return;

            var go = new GameObject("ClearEffects (generated)");
            go.hideFlags = HideFlags.DontSave;      // 実行時生成なのでシーンには保存しない
            go.transform.SetParent(transform, false);
            root = go.transform;
        }

        Instance Rent()
        {
            foreach (var e in pool) if (!e.Active) return e;

            // 足りなければ増やす（大きな消去や連鎖の重なりに対応）
            var go = new GameObject($"Sparkle_{pool.Count}");
            go.hideFlags = HideFlags.DontSave;
            go.transform.SetParent(root, false);
            var created = new Instance
            {
                Renderer = go.AddComponent<SpriteRenderer>(),
            };
            created.Renderer.enabled = false;
            pool.Add(created);
            return created;
        }

        /// <summary>
        /// 使用するマテリアルを決める。未設定なら非ライトのものをその場で作る。
        /// </summary>
        Material ResolveMaterial()
        {
            if (material != null) return material;
            if (fallbackMaterial != null) return fallbackMaterial;

            var shader = Shader.Find("Sprites/Default");
            if (shader == null)
            {
                Debug.LogWarning("[PuyoGame] Sprites/Default シェーダが見つからず、"
                                 + "演出が不透明に描画される可能性があります。", this);
                return null;
            }

            fallbackMaterial = new Material(shader)
            {
                name = "PuyoGame_SparkleUnlit",
                hideFlags = HideFlags.DontSave,
            };
            Debug.LogWarning("[PuyoGame] 演出用マテリアルが未設定のため、非ライトのものを自動生成しました。"
                             + " Tools > PuyoGame > シーンに盤面とUIをセットアップ で設定できます。", this);
            return fallbackMaterial;
        }

        static void Release(Instance e)
        {
            e.Active = false;
            e.Frames = null;
            if (e.Renderer != null) e.Renderer.enabled = false;
        }
    }
}
