using System.Collections.Generic;
using TMPro;
using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace PuyoGame
{
    /// <summary>ゲーム全体の進行状態。</summary>
    public enum GameState
    {
        Title,
        Rules,
        Playing,
        GameOver,
    }

    /// <summary>
    /// 画面遷移（タイトル / プレイ中 / ゲームオーバー）、スコア集計、UI表示を担当する。
    /// 盤面のロジックは持たず、PairFallController のイベントを受けて動く。
    /// </summary>
    [DisallowMultipleComponent]
    public class GameManager : MonoBehaviour
    {
        [Header("参照")]
        [SerializeField] PairFallController controller;

        [Header("UI")]
        [SerializeField] TMP_Text scoreText;
        [Tooltip("スコアの表示枠。タイトル中は隠す。未指定なら scoreText を隠す")]
        [SerializeField] GameObject scoreRoot;
        [Tooltip("タイトル画面（ロゴと開始案内）")]
        [SerializeField] GameObject titleRoot;
        [Tooltip("あそびかたの説明画面")]
        [SerializeField] GameObject rulesRoot;
        [Tooltip("ゲームオーバー表示")]
        [SerializeField] GameObject gameOverRoot;

        [Header("演出")]
        [Tooltip("連鎖時に画面を揺らす")]
        [SerializeField] CameraShake cameraShake;
        [Tooltip("大量消去したときに出すテキスト")]
        [SerializeField] PopupText popupText;
        [Tooltip("効果音。未設定でも無音で動く")]
        [SerializeField] GameAudio gameAudio;
        [Tooltip("BGM。未設定でも無音で動く")]
        [SerializeField] MusicPlayer music;
        [Tooltip("この数以上消すと Nice! を出す")]
        [Min(1)]
        [SerializeField] int niceThreshold = 6;
        [Tooltip("この数以上消すと Great! を出す")]
        [Min(1)]
        [SerializeField] int greatThreshold = 10;
        [SerializeField] Color niceColor = new Color(1f, 0.93f, 0.55f);
        [SerializeField] Color greatColor = new Color(1f, 0.62f, 0.35f);

        [Header("スコア")]
        [SerializeField] int pointsPerBlock = ScoreCalculator.DefaultPointsPerBlock;
        [Tooltip("{0} が現在のスコアに置き換わる")]
        [SerializeField] string scoreFormat = "SCORE\n{0}";

        [Header("ゲームオーバー時のスコア")]
        [Tooltip("ゲームオーバー画面に出す最終スコア。未設定でも動く")]
        [SerializeField] TMP_Text finalScoreText;
        [Tooltip("{0} が最終スコアに置き換わる")]
        [SerializeField] string finalScoreFormat = "SCORE: {0}";
        [Tooltip("0から最終スコアまで数え上げる秒数")]
        [Min(0f)]
        [SerializeField] float scoreCountUpSeconds = 1f;

        /// <summary>現在のスコア。</summary>
        public int Score { get; private set; }

        /// <summary>現在の進行状態。</summary>
        public GameState State { get; private set; } = GameState.Title;

        bool bound;

        int countUpTarget;
        float countUpElapsed;

        /// <summary>最終スコアを数え上げている最中か。</summary>
        public bool IsCountingUpScore { get; private set; }

        /// <summary>ゲームオーバー画面に今表示している数値。</summary>
        public int DisplayedFinalScore { get; private set; }

        void Reset()
        {
            controller = GetComponent<PairFallController>();
        }

        void Awake()
        {
            if (controller == null) controller = GetComponent<PairFallController>();
        }

        void OnEnable()
        {
            Bind();
            EnterTitle();
        }

        void OnDisable()
        {
            Unbind();
        }

        void Update()
        {
            if (IsCountingUpScore) TickScoreCountUp(Time.deltaTime);

            switch (State)
            {
                case GameState.Title:
                    if (IsStartPressed()) ShowRules();
                    break;
                case GameState.Rules:
                    if (IsStartPressed()) StartGame();
                    break;
                case GameState.GameOver:
                    if (IsRestartPressed()) StartGame();
                    break;
            }
        }

        /// <summary>コントローラのイベントを購読する。多重購読はしない。</summary>
        public void Bind()
        {
            if (bound || controller == null) return;
            controller.ChainResolved += AddChainScore;
            controller.GameOverOccurred += OnGameOver;
            bound = true;
        }

        /// <summary>購読を解除する。</summary>
        public void Unbind()
        {
            if (!bound || controller == null) return;
            controller.ChainResolved -= AddChainScore;
            controller.GameOverOccurred -= OnGameOver;
            bound = false;
        }

        /// <summary>タイトル画面に戻す。盤面は空にし、ペアは出さない。</summary>
        public void EnterTitle()
        {
            State = GameState.Title;
            StopScoreCountUp();
            SetScore(0);

            if (controller != null)
            {
                controller.ClearBoard();
                controller.enabled = false;      // タイトル中は落下も入力も止める
            }
            if (popupText != null) popupText.Hide();
            if (cameraShake != null) cameraShake.Stop();
            // ここではまだ鳴らさない。ブラウザは利用者が触るまで音を止めるので、
            // 「はじめる」を押した瞬間から鳴らすほうが確実で、聞き逃しもない。
            if (music != null) music.PlayTrack(MusicPlayer.Track.None);

            ApplyStateToUI();
        }

        /// <summary>あそびかたの説明を出す。ここで初めてBGMを鳴らし始める。</summary>
        public void ShowRules()
        {
            // 押した手応えを返す。ブラウザの音の制限も、この操作をきっかけに外れる。
            if (gameAudio != null) gameAudio.PlayConfirm();

            State = GameState.Rules;
            ApplyStateToUI();
            if (music != null) music.PlayTrack(MusicPlayer.Track.Title);
        }

        /// <summary>ゲームを開始する（説明画面からでもゲームオーバーからでも同じ入口）。</summary>
        public void StartGame()
        {
            // ボタン・スペース・Rのどれで始めても、押した手応えを返す
            if (gameAudio != null) gameAudio.PlayConfirm();

            State = GameState.Playing;
            StopScoreCountUp();
            SetScore(0);
            ApplyStateToUI();
            if (music != null) music.PlayTrack(MusicPlayer.Track.Play);

            if (controller != null)
            {
                controller.enabled = true;
                controller.Restart();
            }
        }

        /// <summary>
        /// 連鎖1回分の得点を加算する。
        /// clearedPerChain は呼び出し側で使い回されるため、保持せずその場で使い切る。
        /// </summary>
        public void AddChainScore(IReadOnlyList<int> clearedPerChain)
        {
            SetScore(Score + ScoreCalculator.TotalScore(clearedPerChain, pointsPerBlock));
            PlayClearEffects(clearedPerChain);
        }

        /// <summary>連鎖数と消去数に応じて、画面の揺れとテキストを出す。</summary>
        void PlayClearEffects(IReadOnlyList<int> clearedPerChain)
        {
            if (clearedPerChain == null || clearedPerChain.Count == 0) return;

            int chain = clearedPerChain.Count;
            int total = 0;
            for (int i = 0; i < clearedPerChain.Count; i++) total += clearedPerChain[i];

            // 1連鎖では揺らさない。連鎖が伸びるほど強く（上限は CameraShake 側）
            if (cameraShake != null) cameraShake.ShakeForChain(chain);

            if (gameAudio != null)
            {
                gameAudio.PlayClear(total);              // 消した数だけ音程が上がる
                if (chain >= 2) gameAudio.PlayChain(chain);
            }

            if (popupText == null) return;
            if (total >= greatThreshold) popupText.Show("Great!", greatColor);
            else if (total >= niceThreshold) popupText.Show("Nice!", niceColor);
        }

        /// <summary>直前の消去で出す文言を返す（動作確認用）。空文字なら演出なし。</summary>
        public string PopupTextFor(int clearedTotal)
        {
            if (clearedTotal >= greatThreshold) return "Great!";
            if (clearedTotal >= niceThreshold) return "Nice!";
            return string.Empty;
        }

        /// <summary>スコアを0に戻して最初から再開する。</summary>
        public void Restart() => StartGame();

        void OnGameOver()
        {
            State = GameState.GameOver;
            ApplyStateToUI();
            StartScoreCountUp();
            if (popupText != null) popupText.Hide();     // 直前の Nice!/Great! が重なるのを防ぐ
            if (gameAudio != null) gameAudio.PlayGameOver();
            if (music != null) music.PlayTrack(MusicPlayer.Track.GameOver);
        }

        void ApplyStateToUI()
        {
            if (titleRoot != null) titleRoot.SetActive(State == GameState.Title);
            if (rulesRoot != null) rulesRoot.SetActive(State == GameState.Rules);
            if (gameOverRoot != null) gameOverRoot.SetActive(State == GameState.GameOver);
            var scoreTarget = scoreRoot != null ? scoreRoot : (scoreText != null ? scoreText.gameObject : null);
            if (scoreTarget != null)
                scoreTarget.SetActive(State != GameState.Title && State != GameState.Rules);
        }

        /// <summary>0から最終スコアまでの数え上げを始める。</summary>
        public void StartScoreCountUp()
        {
            countUpTarget = Score;
            countUpElapsed = 0f;
            IsCountingUpScore = scoreCountUpSeconds > 0f && countUpTarget > 0;
            ShowFinalScore(IsCountingUpScore ? 0 : countUpTarget);
        }

        /// <summary>数え上げを1フレーム分進める。</summary>
        public void TickScoreCountUp(float deltaSeconds)
        {
            if (!IsCountingUpScore) return;

            countUpElapsed += deltaSeconds;
            float t = Mathf.Clamp01(countUpElapsed / Mathf.Max(scoreCountUpSeconds, 0.0001f));
            // 終わり際にゆっくり止まると、数え上がった手応えが出る
            float eased = 1f - (1f - t) * (1f - t);
            ShowFinalScore(Mathf.RoundToInt(countUpTarget * eased));

            if (t < 1f) return;
            IsCountingUpScore = false;
            ShowFinalScore(countUpTarget);      // 端数で目標に届かないことがあるので合わせる
        }

        void StopScoreCountUp()
        {
            IsCountingUpScore = false;
            countUpElapsed = 0f;
            ShowFinalScore(0);
        }

        void ShowFinalScore(int value)
        {
            DisplayedFinalScore = value;
            if (finalScoreText != null) finalScoreText.text = string.Format(finalScoreFormat, value);
        }

        void SetScore(int value)
        {
            Score = value;
            if (scoreText != null) scoreText.text = string.Format(scoreFormat, value);
        }

#if ENABLE_INPUT_SYSTEM
        static bool IsStartPressed()
        {
            var kb = Keyboard.current;
            return kb != null && (kb.spaceKey.wasPressedThisFrame || kb.enterKey.wasPressedThisFrame);
        }

        static bool IsRestartPressed()
        {
            var kb = Keyboard.current;
            return kb != null && kb.rKey.wasPressedThisFrame;
        }
#elif ENABLE_LEGACY_INPUT_MANAGER
        static bool IsStartPressed() => Input.GetKeyDown(KeyCode.Space) || Input.GetKeyDown(KeyCode.Return);
        static bool IsRestartPressed() => Input.GetKeyDown(KeyCode.R);
#else
        static bool IsStartPressed() => false;
        static bool IsRestartPressed() => false;
#endif
    }
}
