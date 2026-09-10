using System;
using System.Collections.Generic;
using System.IO;
using TMPro;
using UnityEditor;
using UnityEditor.Events;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;

namespace PuyoGame.EditorTools
{
    /// <summary>
    /// 盤面・背景・UIのセットアップを手作業でやらなくて済むようにするエディタ拡張。
    /// </summary>
    public static class PuyoGameSetup
    {
        const string BoardObjectName = "Board";
        const string CanvasObjectName = "PuyoUI";
        const string BackgroundObjectName = "Background";
        const string AudioObjectName = "GameAudio";
        const string MusicObjectName = "GameMusic";
        const string PostVolumeName = "PostProcessVolume";
        const string WebGLTemplateName = "PROJECT:PuyoPortrait";
        const string PostProfilePath = "Assets/Settings/PuyoPostProcess.asset";

        const string LogoPath = "Assets/Sprites/UI/logo_puyogame.png";
        const string BackgroundPath = "Assets/Sprites/UI/background_title.png";
        const string GameOverPath = "Assets/Sprites/UI/gameover.png";
        const string PanelPath = "Assets/Sprites/UI/Kenney_Panels/panel_blue.png";
        const string ButtonPath = "Assets/Sprites/UI/Kenney_UIPack/blue_button_rectangle_depth_gradient.png";
        const string RoundButtonPath = "Assets/Sprites/UI/Kenney_UIPack/blue_button_round_depth_flat.png";
        const string IconRoot = "Assets/Sprites/UI/Kenney_Icons";
        const string SparkleRoot = "Assets/Sprites/FX/Sparkles";
        const string PuyoRoot = "Assets/Sprites/Puyo";

        /// <summary>ブロックの種類と、使う写真スプライトの対応。</summary>
        static readonly (PuyoColor color, string path)[] BlockSpriteMap =
        {
            (PuyoColor.Photo1, PuyoRoot + "/puyo_photo1.png"),
            (PuyoColor.Photo2, PuyoRoot + "/puyo_photo2.png"),
            (PuyoColor.Photo3, PuyoRoot + "/puyo_photo3.png"),
        };

        /// <summary>
        /// タイトル画面に漂わせる写真ぷよ。画面比率が変わっても端に寄ったままになるよう、
        /// 位置は中央からの座標ではなくアンカー（画面に対する割合）で持つ。
        /// </summary>
        static readonly (string name, string path, Vector2 anchor, float size)[] TitleDecorLayout =
        {
            ("Float1", PuyoRoot + "/puyo_photo1.png", new Vector2(0.15f, 0.91f), 250f),
            ("Float2", PuyoRoot + "/puyo_photo2.png", new Vector2(0.86f, 0.87f), 210f),
            ("Float3", PuyoRoot + "/puyo_photo3.png", new Vector2(0.16f, 0.19f), 285f),
            ("Float4", PuyoRoot + "/puyo_photo1.png", new Vector2(0.85f, 0.09f), 235f),
        };

        const float TitleDecorAlpha = 0.8f;

        /// <summary>ブロックの種類と、消去演出に使うキラキラのフォルダ名の対応。</summary>
        static readonly (PuyoColor color, string folder)[] SparkleMap =
        {
            (PuyoColor.Photo1, "Red"),
            (PuyoColor.Photo2, "Green"),
            (PuyoColor.Photo3, "Blue"),
        };

        // 盤面を先に決めて、それに合わせてカメラの画角を決める。
        const int BoardColumns = 4;
        const int BoardRows = 8;
        const float CellSize = 1f;

        // 近年のiPhoneの画面比 19.5:9 を基準にする（横÷縦 = 9/19.5 ≒ 0.4615）
        const float DesignAspectWidth = 9f;
        const float DesignAspectHeight = 19.5f;
        const float DesignAspect = DesignAspectWidth / DesignAspectHeight;
        const int DesignScreenWidth = 1170;      // iPhone 13/14 Pro 相当
        const int DesignScreenHeight = 2532;
        const string GameViewSizeName = "iPhone 19.5:9 (PuyoGame)";

        const float WidthFill = 0.85f;           // 盤面が画面幅に占める割合の目安
        const float HeightFill = 0.88f;          // 盤面が画面高さに占める割合の上限（枠のぶん余裕を残す）
        const float BottomReserve = 0.15f;       // 操作ボタンのために画面下部を空ける割合

        /// <summary>
        /// 画面下部の操作ボタン。左から順に並べる。
        /// x はアンカー（画面幅に対する割合）、y は画面下端からのUI座標。
        /// </summary>
        static readonly (string name, string icon, float x, float size, bool mirrored)[] TouchPadLayout =
        {
            ("MoveLeftButton",    "arrowLeft.png",  0.11f, 175f, false),
            ("RotateLeftButton",  "return.png",     0.29f, 150f, false),
            ("SoftDropButton",    "arrowDown.png",  0.50f, 175f, false),
            ("RotateRightButton", "return.png",     0.71f, 150f, true),
            ("MoveRightButton",   "arrowRight.png", 0.89f, 175f, false),
        };

        const float TouchPadY = 195f;            // 画面下端からのUI座標

        [MenuItem("Tools/PuyoGame/シーンに盤面とUIをセットアップ")]
        public static void SetupBoardInScene()
        {
            // 画像のインポート修正は AssetDatabase.Refresh を伴うので、シーンを触る前に済ませる
            PrepareSpriteAssets();
            ConfigurePlayerSettings();
            ConfigureWebGLSettings();

            var view = CreateOrFindBoard();
            ApplyBoardSize(view);
            AssignBlockSprites(view);
            EnsureBoardFrame(view);
            var audio = EnsureGameAudio();
            var music = EnsureMusicPlayer();
            var ctrl = EnsureFallController(view, audio);
            EnsureGameManager(view, ctrl, audio, music);
            EnsureClearEffect(view, ctrl);
            EnsurePointerControls(view, ctrl);
            FitMainCamera(view);
            EnsureBackground();          // カメラ画角が決まってから覆わせる
            EnsureEventSystem();
            EnsurePostProcessing();
            ApplyGameViewAspect();

            Selection.activeGameObject = view.gameObject;

            // 保存まで行う。手動保存を忘れると設定が失われるため。
            var scene = view.gameObject.scene;
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            Debug.Log($"[PuyoGame] 盤面とUIをセットアップして保存しました: {view.Width}x{view.Height} / {scene.path}");
        }

        [MenuItem("Tools/PuyoGame/TextMeshPro の必須リソースを取り込む")]
        public static void ImportTextMeshProResources()
        {
            if (TMP_Settings.instance != null)
            {
                Debug.Log("[PuyoGame] TMP の必須リソースは既に取り込み済みです。");
                return;
            }
            TMP_PackageResourceImporter.ImportResources(true, false, false);
            AssetDatabase.Refresh();
        }

        /// <summary>バッチモードから呼ぶ用。セットアップしてシーンを保存する。</summary>
        public static void SetupBoardAndSaveScene()
        {
            PrepareSpriteAssets();
            ConfigurePlayerSettings();
            ConfigureWebGLSettings();

            var scene = EditorSceneManager.OpenScene("Assets/Scenes/SampleScene.unity");
            var view = CreateOrFindBoard();
            ApplyBoardSize(view);
            AssignBlockSprites(view);
            EnsureBoardFrame(view);
            var audio = EnsureGameAudio();
            var music = EnsureMusicPlayer();
            var ctrl = EnsureFallController(view, audio);
            EnsureGameManager(view, ctrl, audio, music);
            EnsureClearEffect(view, ctrl);
            EnsurePointerControls(view, ctrl);
            FitMainCamera(view);
            EnsureBackground();          // カメラ画角が決まってから覆わせる
            EnsureEventSystem();
            EnsurePostProcessing();
            ApplyGameViewAspect();
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            Debug.Log($"[PuyoGame] シーンに盤面とUIを保存しました: {scene.path}");
        }

        // ---------------- 盤面 ----------------

        static BoardView CreateOrFindBoard()
        {
            var view = UnityEngine.Object.FindAnyObjectByType<BoardView>();
            if (view != null) return view;

            var go = new GameObject(BoardObjectName);
            go.transform.position = Vector3.zero;
            view = go.AddComponent<BoardView>();
            Undo.RegisterCreatedObjectUndo(go, "Create Board");
            return view;
        }

        /// <summary>
        /// 盤面がちょうど収まる画角を求める。
        /// 横幅を埋めるのに必要な大きさと、縦に収めるのに必要な大きさの、厳しい方を採る。
        /// </summary>
        static float ComputeOrthographicSize()
        {
            float boardWidth = BoardColumns * CellSize;
            float boardHeight = BoardRows * CellSize;

            float fromWidth = (boardWidth / WidthFill) / (2f * DesignAspect);
            float fromHeight = (boardHeight / HeightFill) / (2f * (1f - BottomReserve));
            return Mathf.Max(fromWidth, fromHeight);
        }

        /// <summary>盤面のマス数と1マスの大きさを設定する。</summary>
        static void ApplyBoardSize(BoardView view)
        {
            var so = new SerializedObject(view);
            var w = so.FindProperty("width");
            var h = so.FindProperty("height");
            var c = so.FindProperty("cellSize");
            if (w.intValue == BoardColumns && h.intValue == BoardRows
                && Mathf.Abs(c.floatValue - CellSize) < 0.0001f) return;

            w.intValue = BoardColumns;
            h.intValue = BoardRows;
            c.floatValue = CellSize;
            so.ApplyModifiedPropertiesWithoutUndo();
            view.Rebuild();
            Debug.Log($"[PuyoGame] 盤面を {BoardColumns}x{BoardRows} / 1マス={CellSize} に設定しました。");
        }

        /// <summary>盤面に枠と影をつけるコンポーネントを用意する。</summary>
        static void EnsureBoardFrame(BoardView view)
        {
            if (view.GetComponent<BoardFrameView>() == null)
                Undo.AddComponent<BoardFrameView>(view.gameObject);
        }

        /// <summary>ブロックの種類ごとに写真スプライトを割り当てる。</summary>
        static void AssignBlockSprites(BoardView view)
        {
            var so = new SerializedObject(view);
            var arr = so.FindProperty("blockSprites");
            arr.arraySize = BlockSpriteMap.Length;

            int assigned = 0;
            for (int i = 0; i < BlockSpriteMap.Length; i++)
            {
                var element = arr.GetArrayElementAtIndex(i);
                element.FindPropertyRelative("color").enumValueIndex = (int)BlockSpriteMap[i].color;

                var sprite = AssetDatabase.LoadAssetAtPath<Sprite>(BlockSpriteMap[i].path);
                element.FindPropertyRelative("sprite").objectReferenceValue = sprite;
                if (sprite != null) assigned++;
                else Debug.LogWarning($"[PuyoGame] ブロック画像が見つかりません: {BlockSpriteMap[i].path}");
            }
            so.ApplyModifiedPropertiesWithoutUndo();
            Debug.Log($"[PuyoGame] ブロック画像を{assigned}種設定しました。");
        }

        /// <summary>効果音の再生役を用意する。音声ファイルは使わず合成で鳴らす。</summary>
        static GameAudio EnsureGameAudio()
        {
            var go = GameObject.Find(AudioObjectName);
            if (go == null)
            {
                go = new GameObject(AudioObjectName, typeof(AudioSource), typeof(GameAudio));
                Undo.RegisterCreatedObjectUndo(go, "Create GameAudio");
            }

            var src = go.GetComponent<AudioSource>();
            if (src == null) src = go.AddComponent<AudioSource>();
            src.playOnAwake = false;
            src.spatialBlend = 0f;        // 位置に関係なく鳴らす
            src.loop = false;

            // 効果音にはごく薄く残響だけ。高域は残して歯切れを保つ。
            AddAudioEffects(go, reverbLevel: -1300f, lowPassCutoff: 0f);

            // index.html が SendMessage("GameAudio", "UnlockFromBrowser") を呼ぶので、
            // 解除役はこのオブジェクトに置く必要がある。
            if (go.GetComponent<WebAudioUnlock>() == null) go.AddComponent<WebAudioUnlock>();

            var audio = go.GetComponent<GameAudio>();
            if (audio == null) audio = go.AddComponent<GameAudio>();
            return audio;
        }

        /// <summary>BGM の再生役を用意する。切り替え用に AudioSource を2つ持たせる。</summary>
        static MusicPlayer EnsureMusicPlayer()
        {
            var go = GameObject.Find(MusicObjectName);
            if (go == null)
            {
                go = new GameObject(MusicObjectName);
                Undo.RegisterCreatedObjectUndo(go, "Create GameMusic");
            }

            // クロスフェードのため2本必要
            while (go.GetComponents<AudioSource>().Length < 2) go.AddComponent<AudioSource>();
            foreach (var src in go.GetComponents<AudioSource>())
            {
                src.playOnAwake = false;
                src.spatialBlend = 0f;
                src.volume = 0f;
            }

            // 残響と高域の丸めで、打ち込み臭さを減らす
            AddAudioEffects(go, reverbLevel: -400f, lowPassCutoff: 12000f);

            var player = go.GetComponent<MusicPlayer>();
            if (player == null) player = go.AddComponent<MusicPlayer>();

            var unlock = GameObject.Find(AudioObjectName)?.GetComponent<WebAudioUnlock>();
            if (unlock != null)
            {
                var uso = new SerializedObject(unlock);
                uso.FindProperty("music").objectReferenceValue = player;
                uso.ApplyModifiedPropertiesWithoutUndo();
            }
            return player;
        }

        static PairFallController EnsureFallController(BoardView view, GameAudio audio)
        {
            int removed = GameObjectUtility.RemoveMonoBehavioursWithMissingScript(view.gameObject);
            if (removed > 0)
                Debug.Log($"[PuyoGame] 参照切れのコンポーネントを{removed}件削除しました。");

            var controller = view.GetComponent<PairFallController>();
            if (controller == null)
                controller = Undo.AddComponent<PairFallController>(view.gameObject);

            // 開始タイミングは GameManager が握るので、自動開始は切っておく
            var so = new SerializedObject(controller);
            so.FindProperty("autoStartOnPlay").boolValue = false;
            so.FindProperty("gameAudio").objectReferenceValue = audio;
            // 消去に必要な連続数と落下間隔は、スクリプト側の既定に追従させる
            so.FindProperty("minLineLength").intValue = BoardResolver.DefaultMinLineLength;
            so.FindProperty("fallInterval").floatValue = PairFallController.DefaultFallInterval;
            so.ApplyModifiedPropertiesWithoutUndo();
            return controller;
        }

        static GameManager EnsureGameManager(BoardView view, PairFallController ctrl,
                                             GameAudio audio, MusicPlayer music)
        {
            var gm = view.GetComponent<GameManager>();
            if (gm == null) gm = Undo.AddComponent<GameManager>(view.gameObject);

            BuildUI(out var scoreText, out var scoreRoot, out var titleRoot, out var gameOverRoot,
                    out var finalScoreText, out var startButton, out var popup, out var touchPad);

            var pso = new SerializedObject(touchPad);
            pso.FindProperty("controller").objectReferenceValue = ctrl;
            pso.FindProperty("gameManager").objectReferenceValue = gm;
            pso.ApplyModifiedPropertiesWithoutUndo();

            // ボタンから GameManager.StartGame を呼ぶ（重複登録しないよう一度クリアする）
            while (startButton.onClick.GetPersistentEventCount() > 0)
                UnityEventTools.RemovePersistentListener(startButton.onClick, 0);
            UnityEventTools.AddPersistentListener(startButton.onClick, gm.StartGame);

            var so = new SerializedObject(gm);
            so.FindProperty("controller").objectReferenceValue = ctrl;
            so.FindProperty("scoreText").objectReferenceValue = scoreText;
            so.FindProperty("scoreRoot").objectReferenceValue = scoreRoot;
            so.FindProperty("titleRoot").objectReferenceValue = titleRoot;
            so.FindProperty("gameOverRoot").objectReferenceValue = gameOverRoot;
            so.FindProperty("finalScoreText").objectReferenceValue = finalScoreText;
            so.FindProperty("popupText").objectReferenceValue = popup;
            so.FindProperty("cameraShake").objectReferenceValue = EnsureCameraShake();
            so.FindProperty("gameAudio").objectReferenceValue = audio;
            so.FindProperty("music").objectReferenceValue = music;
            so.ApplyModifiedPropertiesWithoutUndo();
            return gm;
        }

        // ---------------- 消去エフェクト ----------------

        /// <summary>
        /// 使用する画像のインポート設定をまとめて整える。
        /// AssetDatabase.Refresh を伴うので、シーンを編集する前に呼ぶこと。
        /// </summary>
        static void PrepareSpriteAssets()
        {
            var sparkles = new List<string>();
            foreach (var (_, folder) in SparkleMap) sparkles.AddRange(SparklePaths(folder));
            FixSpriteImportersBatched(sparkles, fullRect: true);

            var others = new List<string> { LogoPath, BackgroundPath, GameOverPath };
            foreach (var (_, path) in BlockSpriteMap) others.Add(path);
            FixSpriteImportersBatched(others, fullRect: false);
        }

        static ClearEffectPlayer EnsureClearEffect(BoardView view, PairFallController ctrl)
        {
            var player = view.GetComponent<ClearEffectPlayer>();
            if (player == null) player = Undo.AddComponent<ClearEffectPlayer>(view.gameObject);

            var so = new SerializedObject(player);
            so.FindProperty("boardView").objectReferenceValue = view;

            // 既定は URP の Sprite-Lit-Default で2Dライトの影響を受けるため、
            // パーティクル演出には非ライトの Sprites-Default を使う
            var unlit = AssetDatabase.GetBuiltinExtraResource<Material>("Sprites-Default.mat");
            if (unlit == null) Debug.LogWarning("[PuyoGame] Sprites-Default.mat が取得できませんでした。");
            so.FindProperty("material").objectReferenceValue = unlit;

            var sets = so.FindProperty("sparkleSets");
            sets.arraySize = SparkleMap.Length;
            int totalFrames = 0;
            for (int i = 0; i < SparkleMap.Length; i++)
            {
                var element = sets.GetArrayElementAtIndex(i);
                element.FindPropertyRelative("color").enumValueIndex = (int)SparkleMap[i].color;

                var framePaths = SparklePaths(SparkleMap[i].folder);
                var frames = element.FindPropertyRelative("frames");
                frames.arraySize = framePaths.Count;
                for (int j = 0; j < framePaths.Count; j++)
                {
                    var sprite = AssetDatabase.LoadAssetAtPath<Sprite>(framePaths[j]);
                    frames.GetArrayElementAtIndex(j).objectReferenceValue = sprite;
                    if (sprite != null) totalFrames++;
                }
            }
            so.ApplyModifiedPropertiesWithoutUndo();

            var cso = new SerializedObject(ctrl);
            cso.FindProperty("effectPlayer").objectReferenceValue = player;
            cso.ApplyModifiedPropertiesWithoutUndo();

            Debug.Log($"[PuyoGame] 消去エフェクトを設定しました: {SparkleMap.Length}種 / 計{totalFrames}コマ");
            return player;
        }

        /// <summary>色フォルダ内のコマ画像を、ファイル名順（= コマ順）に返す。</summary>
        static List<string> SparklePaths(string folder)
        {
            var result = new List<string>();
            string dir = $"{SparkleRoot}/{folder}";
            string full = Path.Combine(Directory.GetCurrentDirectory(), dir);
            if (!Directory.Exists(full))
            {
                Debug.LogWarning($"[PuyoGame] コマ画像のフォルダが見つかりません: {dir}");
                return result;
            }

            var files = Directory.GetFiles(full, "*.png");
            Array.Sort(files, StringComparer.Ordinal);
            foreach (var f in files) result.Add($"{dir}/{Path.GetFileName(f)}");
            return result;
        }

        static void FixSpriteImportersBatched(List<string> paths, bool fullRect)
        {
            int changed = 0;
            AssetDatabase.StartAssetEditing();
            try
            {
                foreach (var path in paths) if (FixSpriteImporter(path, null, fullRect)) changed++;
            }
            finally
            {
                AssetDatabase.StopAssetEditing();
            }
            if (changed > 0)
            {
                AssetDatabase.Refresh();
                Debug.Log($"[PuyoGame] 画像のインポート設定を{changed}件修正しました。");
            }
        }

        // ---------------- 背景 ----------------

        static void EnsureBackground()
        {
            var go = GameObject.Find(BackgroundObjectName);
            if (go == null)
            {
                go = new GameObject(BackgroundObjectName, typeof(SpriteRenderer), typeof(BackgroundView));
                Undo.RegisterCreatedObjectUndo(go, "Create Background");
            }

            var sr = go.GetComponent<SpriteRenderer>() ?? go.AddComponent<SpriteRenderer>();
            sr.sprite = LoadSprite(BackgroundPath);

            var bv = go.GetComponent<BackgroundView>() ?? go.AddComponent<BackgroundView>();
            bv.Apply();
        }

        // ---------------- UI ----------------

        /// <summary>メインカメラに画面シェイクを付ける。</summary>
        static CameraShake EnsureCameraShake()
        {
            var cam = Camera.main;
            if (cam == null) return null;
            var shake = cam.GetComponent<CameraShake>();
            if (shake == null) shake = Undo.AddComponent<CameraShake>(cam.gameObject);
            return shake;
        }

        static void BuildUI(out TMP_Text scoreText, out GameObject scoreRoot,
                            out GameObject titleRoot, out GameObject gameOverRoot,
                            out TMP_Text finalScoreText,
                            out Button startButton, out PopupText popup,
                            out TouchControlPad touchPad)
        {
            var canvasGo = GameObject.Find(CanvasObjectName);
            if (canvasGo == null)
            {
                canvasGo = new GameObject(CanvasObjectName,
                    typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
                Undo.RegisterCreatedObjectUndo(canvasGo, "Create UI");
            }

            var canvas = canvasGo.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 100;

            var scaler = canvasGo.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1080f, 1920f);
            scaler.matchWidthOrHeight = 0.5f;

            // --- スコア（画面左上、背景パネル付きで背景画像に負けないようにする） ---
            // 旧レイアウトでは ScoreText が Canvas 直下にあった。パネル配下へ移したので取り残しを消す。
            RemoveChild(canvasGo.transform, "ScoreText");

            var panel = FindOrCreateImage(canvasGo.transform, "ScorePanel", PanelPath,
                                          new Vector4(20f, 20f, 20f, 20f));
            panel.type = Image.Type.Sliced;      // 9スライスなので拡大しても角が崩れない
            panel.preserveAspect = false;
            panel.color = new Color(1f, 1f, 1f, 0.92f);
            scoreRoot = panel.gameObject;

            var prt = panel.rectTransform;
            prt.anchorMin = new Vector2(0f, 1f);
            prt.anchorMax = new Vector2(0f, 1f);
            prt.pivot = new Vector2(0f, 1f);
            prt.anchoredPosition = new Vector2(32f, -32f);
            prt.sizeDelta = new Vector2(420f, 190f);

            scoreText = FindOrCreateText(panel.transform, "ScoreText");
            var srt = scoreText.rectTransform;
            srt.anchorMin = Vector2.zero;
            srt.anchorMax = Vector2.one;
            srt.offsetMin = new Vector2(28f, 24f);
            srt.offsetMax = new Vector2(-28f, -20f);
            scoreText.fontSize = 60f;
            scoreText.alignment = TextAlignmentOptions.TopLeft;
            scoreText.color = Color.white;
            scoreText.text = "SCORE\n0";

            // --- タイトル（ロゴ＋開始案内） ---
            titleRoot = FindOrCreateRoot(canvasGo.transform, "TitleRoot");
            CreateDimPanel(titleRoot.transform, 0.45f);

            var decor = BuildTitleDecor(titleRoot.transform);

            var logo = FindOrCreateImage(titleRoot.transform, "LogoImage", LogoPath);
            SetFitted(logo.rectTransform, new Vector2(0f, 320f), 900f, 620f, logo.sprite);

            startButton = BuildStartButton(titleRoot.transform);

            var startText = FindOrCreateText(titleRoot.transform, "StartText");
            SetCentered(startText.rectTransform, new Vector2(0f, -330f), new Vector2(900f, 100f));
            startText.fontSize = 44f;
            startText.alignment = TextAlignmentOptions.Center;
            startText.color = new Color(1f, 1f, 1f, 0.85f);
            startText.text = "or Press SPACE";

            // --- ゲームオーバー（画像＋再開案内） ---
            gameOverRoot = FindOrCreateRoot(canvasGo.transform, "GameOverRoot");
            CreateDimPanel(gameOverRoot.transform, 0.6f);
            RemoveChild(gameOverRoot.transform, "GameOverText");     // テキスト版を画像版に置き換える

            var overImage = FindOrCreateImage(gameOverRoot.transform, "GameOverImage", GameOverPath);
            SetFitted(overImage.rectTransform, new Vector2(0f, 140f), 900f, 560f, overImage.sprite);

            finalScoreText = FindOrCreateText(gameOverRoot.transform, "FinalScoreText");
            SetCentered(finalScoreText.rectTransform, new Vector2(0f, -170f), new Vector2(900f, 140f));
            finalScoreText.fontSize = 96f;
            finalScoreText.alignment = TextAlignmentOptions.Center;
            finalScoreText.fontStyle = FontStyles.Bold;
            finalScoreText.color = new Color(1f, 0.92f, 0.55f);
            finalScoreText.raycastTarget = false;
            finalScoreText.text = "SCORE: 0";

            var hint = FindOrCreateText(gameOverRoot.transform, "RestartText");
            SetCentered(hint.rectTransform, new Vector2(0f, -330f), new Vector2(900f, 120f));
            hint.fontSize = 52f;
            hint.alignment = TextAlignmentOptions.Center;
            hint.color = Color.white;
            hint.text = "Tap or Press R to Restart";

            // --- 大量消去のポップアップ（中央やや上、既定は非表示） ---
            var popupRoot = FindOrCreateRoot(canvasGo.transform, "PopupRoot");
            var popupLabel = FindOrCreateText(popupRoot.transform, "PopupLabel");
            SetCentered(popupLabel.rectTransform, new Vector2(0f, 300f), new Vector2(900f, 240f));
            popupLabel.fontSize = 140f;
            popupLabel.alignment = TextAlignmentOptions.Center;
            popupLabel.fontStyle = FontStyles.Bold;
            popupLabel.color = Color.white;
            popupLabel.raycastTarget = false;
            popupLabel.text = "Nice!";

            popup = popupRoot.GetComponent<PopupText>();
            if (popup == null) popup = popupRoot.AddComponent<PopupText>();
            var pso = new SerializedObject(popup);
            pso.FindProperty("label").objectReferenceValue = popupLabel;
            pso.ApplyModifiedPropertiesWithoutUndo();
            popupLabel.gameObject.SetActive(false);

            // 手前ほど後ろの兄弟。装飾がロゴやボタンを隠さないよう、順番を明示しておく。
            titleRoot.transform.Find("Dim").SetSiblingIndex(0);
            decor.transform.SetSiblingIndex(1);
            logo.transform.SetSiblingIndex(2);
            startButton.transform.SetSiblingIndex(3);
            startText.transform.SetSiblingIndex(4);

            touchPad = BuildTouchPad(canvasGo);

            titleRoot.SetActive(true);        // 起動時はタイトルから始まる
            gameOverRoot.SetActive(false);
        }

        /// <summary>
        /// 画面下部のタッチ操作ボタンを組み立てる。
        /// スマホにはキーボードが無いので、これが無いと遊べない。
        /// </summary>
        static TouchControlPad BuildTouchPad(GameObject canvasGo)
        {
            var root = FindOrCreateRoot(canvasGo.transform, "TouchPad");
            root.transform.SetAsFirstSibling();      // タイトルやゲームオーバーより奥に置く

            var buttons = new HoldButton[TouchPadLayout.Length];
            for (int i = 0; i < TouchPadLayout.Length; i++)
            {
                var item = TouchPadLayout[i];

                var img = FindOrCreateImage(root.transform, item.name, RoundButtonPath);
                img.raycastTarget = true;            // ここで指を受ける
                img.preserveAspect = true;
                img.color = new Color(1f, 1f, 1f, 0.88f);

                var rt = img.rectTransform;
                rt.anchorMin = new Vector2(item.x, 0f);
                rt.anchorMax = new Vector2(item.x, 0f);
                rt.pivot = new Vector2(0.5f, 0.5f);
                rt.anchoredPosition = new Vector2(0f, TouchPadY);
                rt.sizeDelta = new Vector2(item.size, item.size);

                var icon = FindOrCreateImage(img.transform, "Icon", IconRoot + "/" + item.icon);
                icon.raycastTarget = false;
                icon.color = new Color(1f, 1f, 1f, 0.95f);
                var irt = icon.rectTransform;
                irt.anchorMin = new Vector2(0.5f, 0.5f);
                irt.anchorMax = new Vector2(0.5f, 0.5f);
                irt.pivot = new Vector2(0.5f, 0.5f);
                irt.anchoredPosition = new Vector2(0f, item.size * 0.04f);   // 立体感の分だけ上へ
                irt.sizeDelta = new Vector2(item.size * 0.52f, item.size * 0.52f);
                // 右回転は、同じ矢印を左右反転して使う
                irt.localScale = new Vector3(item.mirrored ? -1f : 1f, 1f, 1f);

                var hold = img.GetComponent<HoldButton>();
                if (hold == null) hold = img.gameObject.AddComponent<HoldButton>();
                buttons[i] = hold;
            }

            var pad = canvasGo.GetComponent<TouchControlPad>();
            if (pad == null) pad = canvasGo.AddComponent<TouchControlPad>();

            // パッド自体は消したり出したりするので、制御役は常に生きている Canvas に置く
            var so = new SerializedObject(pad);
            so.FindProperty("root").objectReferenceValue = root;
            so.FindProperty("moveLeft").objectReferenceValue = buttons[0];
            so.FindProperty("rotateLeft").objectReferenceValue = buttons[1];
            so.FindProperty("softDrop").objectReferenceValue = buttons[2];
            so.FindProperty("rotateRight").objectReferenceValue = buttons[3];
            so.FindProperty("moveRight").objectReferenceValue = buttons[4];
            so.ApplyModifiedPropertiesWithoutUndo();

            root.SetActive(false);        // プレイ中だけ出す
            Debug.Log($"[PuyoGame] 操作ボタンを{TouchPadLayout.Length}個配置しました。");
            return pad;
        }

        /// <summary>タイトル画面の背景で漂う写真ぷよを用意する。</summary>
        static TitlePuyoDecor BuildTitleDecor(Transform parent)
        {
            var root = FindOrCreateRoot(parent, "TitleDecor");

            foreach (var item in TitleDecorLayout)
            {
                var img = FindOrCreateImage(root.transform, item.name, item.path);
                img.raycastTarget = false;                     // STARTボタンの邪魔をしない
                img.color = new Color(1f, 1f, 1f, TitleDecorAlpha);

                var rt = img.rectTransform;
                rt.anchorMin = item.anchor;
                rt.anchorMax = item.anchor;
                rt.pivot = new Vector2(0.5f, 0.5f);
                rt.anchoredPosition = Vector2.zero;
                rt.sizeDelta = new Vector2(item.size, item.size);
            }

            // 表に無い残骸があれば片付ける
            for (int i = root.transform.childCount - 1; i >= 0; i--)
            {
                var child = root.transform.GetChild(i);
                if (System.Array.FindIndex(TitleDecorLayout, e => e.name == child.name) < 0)
                    UnityEngine.Object.DestroyImmediate(child.gameObject);
            }

            var decor = root.GetComponent<TitlePuyoDecor>();
            if (decor == null) decor = root.AddComponent<TitlePuyoDecor>();
            decor.Collect();
            Debug.Log($"[PuyoGame] タイトルに写真ぷよを{TitleDecorLayout.Length}個配置しました"
                      + $"（不透明度 {TitleDecorAlpha:P0}）。");
            return decor;
        }

        /// <summary>Kenney のボタン画像を使ったスタートボタンを作る。</summary>
        static Button BuildStartButton(Transform parent)
        {
            var img = FindOrCreateImage(parent, "StartButton", ButtonPath,
                                        new Vector4(20f, 24f, 20f, 20f));
            img.type = Image.Type.Sliced;      // 9スライスなので拡大しても角が崩れない
            img.preserveAspect = false;
            img.raycastTarget = true;          // クリックを受け取る
            SetCentered(img.rectTransform, new Vector2(0f, -140f), new Vector2(440f, 140f));

            var button = img.GetComponent<Button>();
            if (button == null) button = img.gameObject.AddComponent<Button>();
            button.targetGraphic = img;
            button.transition = Selectable.Transition.ColorTint;
            var colors = button.colors;
            colors.highlightedColor = new Color(1f, 1f, 1f, 1f);
            colors.pressedColor = new Color(0.8f, 0.8f, 0.8f, 1f);
            button.colors = colors;

            // ラベルは既定フォント（LiberationSans）に日本語がないため英字にする
            var label = FindOrCreateText(img.transform, "Label");
            var lrt = label.rectTransform;
            lrt.anchorMin = Vector2.zero;
            lrt.anchorMax = Vector2.one;
            lrt.offsetMin = new Vector2(20f, 20f);
            lrt.offsetMax = new Vector2(-20f, -28f);
            label.fontSize = 60f;
            label.alignment = TextAlignmentOptions.Center;
            label.color = Color.white;
            label.raycastTarget = false;
            label.text = "START";

            return button;
        }

        /// <summary>
        /// AudioSource に残響とローパスをかける。
        /// AudioMixer のアセットはスクリプトから作れないため、同等の効果をフィルタで得る。
        /// </summary>
        static void AddAudioEffects(GameObject go, float reverbLevel, float lowPassCutoff)
        {
            var reverb = go.GetComponent<AudioReverbFilter>();
            if (reverb == null) reverb = go.AddComponent<AudioReverbFilter>();
            reverb.reverbPreset = AudioReverbPreset.User;
            reverb.dryLevel = 0f;              // 元の音はそのまま通す
            reverb.room = -900f;
            reverb.roomHF = -700f;
            reverb.decayTime = 1.1f;
            reverb.decayHFRatio = 0.7f;
            reverb.reflectionsLevel = -1600f;
            reverb.reflectionsDelay = 0.01f;
            reverb.reverbLevel = reverbLevel;
            reverb.reverbDelay = 0.02f;

            var lowPass = go.GetComponent<AudioLowPassFilter>();
            if (lowPassCutoff <= 0f)
            {
                if (lowPass != null) Undo.DestroyObjectImmediate(lowPass);
                return;
            }
            if (lowPass == null) lowPass = go.AddComponent<AudioLowPassFilter>();
            lowPass.cutoffFrequency = lowPassCutoff;
            lowPass.lowpassResonanceQ = 1f;
        }

        /// <summary>
        /// URP のポストプロセスを用意する。Bloom・色調補正・周辺減光を軽くかける。
        /// </summary>
        static void EnsurePostProcessing()
        {
            var profile = AssetDatabase.LoadAssetAtPath<VolumeProfile>(PostProfilePath);
            if (profile == null)
            {
                profile = ScriptableObject.CreateInstance<VolumeProfile>();
                AssetDatabase.CreateAsset(profile, PostProfilePath);
            }
            // 保存に失敗した設定（参照切れ）が残っていると効果が出ないので取り除く
            profile.components.RemoveAll(c => c == null);

            // 光沢やキラキラを少しにじませる
            var bloom = GetOrAddSetting<Bloom>(profile);
            bloom.threshold.Override(0.72f);
            bloom.intensity.Override(1.05f);
            bloom.scatter.Override(0.70f);
            bloom.tint.Override(new Color(1f, 0.97f, 0.90f));

            // 彩度とコントラストを少しだけ上げる
            var color = GetOrAddSetting<ColorAdjustments>(profile);
            color.postExposure.Override(0.10f);
            color.contrast.Override(12f);
            color.saturation.Override(20f);

            // 画面端を落として中央に視線を集める
            var vignette = GetOrAddSetting<Vignette>(profile);
            vignette.intensity.Override(0.26f);   // 盤面がもともと暗いので控えめに
            vignette.smoothness.Override(0.45f);
            vignette.rounded.Override(false);
            vignette.color.Override(new Color(0.03f, 0.02f, 0.08f));

            EditorUtility.SetDirty(profile);
            AssetDatabase.SaveAssets();

            var go = GameObject.Find(PostVolumeName);
            if (go == null)
            {
                go = new GameObject(PostVolumeName);
                Undo.RegisterCreatedObjectUndo(go, "Create Post Process Volume");
            }
            var volume = go.GetComponent<Volume>();
            if (volume == null) volume = go.AddComponent<Volume>();
            volume.isGlobal = true;
            volume.priority = 0f;
            volume.weight = 1f;
            volume.sharedProfile = profile;

            var cam = Camera.main;
            if (cam == null) return;
            cam.allowHDR = true;                     // Bloom を綺麗に出すため
            var data = cam.GetUniversalAdditionalCameraData();
            if (data != null) data.renderPostProcessing = true;

            Debug.Log($"[PuyoGame] ポストプロセスを設定しました（設定数 {profile.components.Count}）。");
        }

        /// <summary>
        /// プロファイルから設定を取り出す。無ければ作ってアセットの一部として保存する。
        /// AddObjectToAsset を忘れると、参照が保存されず効果が出なくなる。
        /// </summary>
        static T GetOrAddSetting<T>(VolumeProfile profile) where T : VolumeComponent
        {
            if (!profile.TryGet<T>(out var setting) || setting == null)
            {
                setting = ScriptableObject.CreateInstance<T>();
                setting.name = typeof(T).Name;
                setting.hideFlags = HideFlags.HideInHierarchy | HideFlags.HideInInspector;
                profile.components.Add(setting);
                AssetDatabase.AddObjectToAsset(setting, profile);
            }
            setting.active = true;
            return setting;
        }

        /// <summary>タップ・クリック操作を受け付けるコンポーネントを用意する。</summary>
        static void EnsurePointerControls(BoardView view, PairFallController ctrl)
        {
            var controls = view.GetComponent<PointerControls>();
            if (controls == null) controls = Undo.AddComponent<PointerControls>(view.gameObject);

            var so = new SerializedObject(controls);
            so.FindProperty("controller").objectReferenceValue = ctrl;
            so.FindProperty("gameManager").objectReferenceValue = view.GetComponent<GameManager>();
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        // ---------------- 画面比率 ----------------

        /// <summary>
        /// 縦画面固定と、19.5:9 を想定した既定解像度を Player Settings に書き込む。
        /// </summary>
        static void ConfigurePlayerSettings()
        {
            PlayerSettings.defaultInterfaceOrientation = UIOrientation.Portrait;
            PlayerSettings.allowedAutorotateToPortrait = true;
            PlayerSettings.allowedAutorotateToPortraitUpsideDown = false;
            PlayerSettings.allowedAutorotateToLandscapeLeft = false;
            PlayerSettings.allowedAutorotateToLandscapeRight = false;
            PlayerSettings.defaultScreenWidth = DesignScreenWidth;
            PlayerSettings.defaultScreenHeight = DesignScreenHeight;
            PlayerSettings.defaultIsNativeResolution = false;
            PlayerSettings.resizableWindow = true;

            EditProjectSettings(so =>
            {
                SetInt(so, "defaultScreenOrientation", 0);           // Portrait
                SetBool(so, "allowedAutorotateToPortrait", true);
                SetBool(so, "allowedAutorotateToPortraitUpsideDown", false);
                SetBool(so, "allowedAutorotateToLandscapeLeft", false);
                SetBool(so, "allowedAutorotateToLandscapeRight", false);

                // PC で実行したときも縦長で立ち上がるようにする
                SetInt(so, "defaultScreenWidth", DesignScreenWidth);
                SetInt(so, "defaultScreenHeight", DesignScreenHeight);
                SetBool(so, "defaultIsNativeResolution", false);
                SetBool(so, "resizableWindow", true);
            });

            Debug.Log($"[PuyoGame] 画面を縦向き固定 / {DesignScreenWidth}x{DesignScreenHeight}"
                      + $"（{DesignAspectWidth}:{DesignAspectHeight}）に設定しました。");
        }

        // ---------------- WebGL ----------------

        [MenuItem("Tools/PuyoGame/WebGL向けの設定を適用")]
        public static void ConfigureWebGLSettings()
        {
            // 通常のAPIでも設定しておく。エディタを開いたまま操作したときに
            // Inspector の表示と食い違わないようにするため。
            // ただしこれだけではファイルに保存されないので、続けて SerializedObject でも書く。
            PlayerSettings.WebGL.template = WebGLTemplateName;
            PlayerSettings.WebGL.compressionFormat = WebGLCompressionFormat.Gzip;
            PlayerSettings.WebGL.decompressionFallback = true;
            PlayerSettings.WebGL.dataCaching = true;
            PlayerSettings.WebGL.linkerTarget = WebGLLinkerTarget.Wasm;
            PlayerSettings.WebGL.exceptionSupport = WebGLExceptionSupport.ExplicitlyThrownExceptionsOnly;
            PlayerSettings.WebGL.powerPreference = WebGLPowerPreference.HighPerformance;
            PlayerSettings.runInBackground = true;

            EditProjectSettings(so =>
            {
                // 縦長固定の自作テンプレートを使う。画面比が変わっても盤面が切れない。
                SetString(so, "webGLTemplate", WebGLTemplateName);
                SetInt(so, "defaultScreenWidthWeb", DesignScreenWidth);
                SetInt(so, "defaultScreenHeightWeb", DesignScreenHeight);

                // どんな静的ホストでも動くように、解凍の代替手段を入れておく。
                // Brotli はサーバ側の設定が要るので Gzip にする。
                SetInt(so, "webGLCompressionFormat", (int)WebGLCompressionFormat.Gzip);
                SetBool(so, "webGLDecompressionFallback", true);
                SetBool(so, "webGLDataCaching", true);
                SetInt(so, "webGLLinkerTarget", (int)WebGLLinkerTarget.Wasm);
                SetInt(so, "webGLExceptionSupport",
                       (int)WebGLExceptionSupport.ExplicitlyThrownExceptionsOnly);
                SetInt(so, "webGLPowerPreference", (int)WebGLPowerPreference.HighPerformance);

                // タブが後ろに回っても止めない（音と落下が飛ばないように）
                SetBool(so, "runInBackground", true);
            });

            // URP をリニア色空間で使うので WebGL2 が要る。Unity 6 の既定がまさに WebGL2。
            var apis = PlayerSettings.GetGraphicsAPIs(BuildTarget.WebGL);
            Debug.Log($"[PuyoGame] WebGL の設定を適用しました: テンプレート={PlayerSettings.WebGL.template} "
                      + $"/ 圧縮={PlayerSettings.WebGL.compressionFormat}"
                      + $"（代替解凍 {PlayerSettings.WebGL.decompressionFallback}）"
                      + $"/ 色空間={PlayerSettings.colorSpace} / 描画API={string.Join(", ", apis)}");
        }

        [MenuItem("Tools/PuyoGame/WebGLビルドを作る")]
        public static void BuildWebGL()
        {
            ConfigureWebGLSettings();

            const string output = "Build/WebGL";
            var options = new BuildPlayerOptions
            {
                scenes = new[] { "Assets/Scenes/SampleScene.unity" },
                locationPathName = output,
                target = BuildTarget.WebGL,
                targetGroup = BuildTargetGroup.WebGL,
                options = BuildOptions.None,
            };

            var report = BuildPipeline.BuildPlayer(options);
            var summary = report.summary;
            Debug.Log($"[PuyoGame] WebGLビルド: {summary.result} "
                      + $"/ {summary.totalSize / 1024 / 1024}MB / {summary.totalTime.TotalSeconds:F0}秒 → {output}");

            if (summary.result != UnityEditor.Build.Reporting.BuildResult.Succeeded)
                throw new System.Exception($"WebGLビルドに失敗しました: {summary.result}");
        }

        /// <summary>
        /// Player Settings を SerializedObject 経由で書き換える。
        /// PlayerSettings.* のプロパティに代入するだけでは、バッチモードだと
        /// ファイルに書き出されないまま終了してしまうため。
        /// </summary>
        static void EditProjectSettings(Action<SerializedObject> edit)
        {
            foreach (var settings in Resources.FindObjectsOfTypeAll<PlayerSettings>())
            {
                var so = new SerializedObject(settings);
                edit(so);
                so.ApplyModifiedPropertiesWithoutUndo();
            }
            AssetDatabase.SaveAssets();
        }

        // 項目名が変わったときに黙って効かなくなるのを避けるため、見つからなければ警告を出す
        static SerializedProperty Find(SerializedObject so, string name)
        {
            var p = so.FindProperty(name);
            if (p == null) Debug.LogWarning($"[PuyoGame] Player Settings に「{name}」が見つかりませんでした。");
            return p;
        }

        static void SetInt(SerializedObject so, string name, int value)
        {
            var p = Find(so, name);
            if (p != null) p.intValue = value;
        }

        static void SetBool(SerializedObject so, string name, bool value)
        {
            var p = Find(so, name);
            if (p != null) p.boolValue = value;
        }

        static void SetString(SerializedObject so, string name, string value)
        {
            var p = Find(so, name);
            if (p != null) p.stringValue = value;
        }

        [MenuItem("Tools/PuyoGame/Game ビューを 19.5:9 にする")]
        public static void ApplyGameViewAspect()
        {
            try
            {
                int index = EnsureGameViewSize();
                if (index < 0)
                {
                    Debug.LogWarning("[PuyoGame] Game ビューの解像度一覧を扱えませんでした。手動で 19.5:9 を選んでください。");
                    return;
                }
                Debug.Log($"[PuyoGame] Game ビューに「{GameViewSizeName}」を {index} 番目として登録しました。");

                if (Application.isBatchMode) return;   // バッチモードには Game ビューの窓が無い

                var gameViewType = typeof(Editor).Assembly.GetType("UnityEditor.GameView");
                var window = EditorWindow.GetWindow(gameViewType, false, null, false);
                var callback = gameViewType.GetMethod(
                    "SizeSelectionCallback",
                    System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public
                    | System.Reflection.BindingFlags.NonPublic);
                callback.Invoke(window, new object[] { index, null });
                Debug.Log($"[PuyoGame] Game ビューを「{GameViewSizeName}」に切り替えました。");
            }
            catch (System.Exception e)
            {
                // Game ビューの API は内部向けなので、失敗しても他の設定は活かす
                Debug.LogWarning($"[PuyoGame] Game ビューの切り替えに失敗しました。手動で選んでください: {e.Message}");
            }
        }

        /// <summary>
        /// Game ビューの解像度一覧に 19.5:9 を登録し、その位置を返す。
        /// GameViewSizes は内部APIのため、リフレクション経由で触る。
        /// </summary>
        static int EnsureGameViewSize()
        {
            var asm = typeof(Editor).Assembly;
            var sizesType = asm.GetType("UnityEditor.GameViewSizes");
            var groupType = asm.GetType("UnityEditor.GameViewSizeGroup");
            var sizeType = asm.GetType("UnityEditor.GameViewSize");
            var sizeTypeEnum = asm.GetType("UnityEditor.GameViewSizeType");
            if (sizesType == null || groupType == null || sizeType == null) return -1;

            var singleton = typeof(ScriptableSingleton<>).MakeGenericType(sizesType);
            var instance = singleton.GetProperty("instance").GetValue(null);
            var group = sizesType.GetProperty("currentGroup").GetValue(instance);

            int total = (int)groupType.GetMethod("GetTotalCount").Invoke(group, null);
            var getSize = groupType.GetMethod("GetGameViewSize");
            var baseTextProp = sizeType.GetProperty("baseText");

            for (int i = 0; i < total; i++)
            {
                var size = getSize.Invoke(group, new object[] { i });
                if ((string)baseTextProp.GetValue(size) == GameViewSizeName) return i;
            }

            // FixedResolution(=0) で 1170x2532 を追加する
            var ctor = sizeType.GetConstructor(new[] { sizeTypeEnum, typeof(int), typeof(int), typeof(string) });
            var fixedResolution = System.Enum.ToObject(sizeTypeEnum, 0);
            var added = ctor.Invoke(new object[]
            {
                fixedResolution, DesignScreenWidth, DesignScreenHeight, GameViewSizeName
            });
            groupType.GetMethod("AddCustomSize").Invoke(group, new[] { added });
            sizesType.GetMethod("SaveToHDD").Invoke(instance, null);
            return total;
        }

        /// <summary>UIのクリック受付に必要な EventSystem を用意する。</summary>
        static void EnsureEventSystem()
        {
            var es = UnityEngine.Object.FindAnyObjectByType<EventSystem>();
            if (es == null)
            {
                var go = new GameObject("EventSystem", typeof(EventSystem));
                Undo.RegisterCreatedObjectUndo(go, "Create EventSystem");
                es = go.GetComponent<EventSystem>();
            }

            // 新 Input System 専用プロジェクトなので、旧モジュールは使えない
            var legacy = es.GetComponent<StandaloneInputModule>();
            if (legacy != null) UnityEngine.Object.DestroyImmediate(legacy);

            var module = es.GetComponent<InputSystemUIInputModule>();
            if (module == null)
            {
                module = es.gameObject.AddComponent<InputSystemUIInputModule>();
                module.AssignDefaultActions();
            }
        }

        static GameObject FindOrCreateRoot(Transform parent, string name)
        {
            var tf = parent.Find(name);
            if (tf == null)
            {
                var created = new GameObject(name, typeof(RectTransform));
                created.transform.SetParent(parent, false);
                tf = created.transform;
            }
            var rt = (RectTransform)tf;
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
            return tf.gameObject;
        }

        /// <summary>盤面が透けつつ文字が読めるよう、半透明の暗幕を最背面に敷く。</summary>
        static void CreateDimPanel(Transform parent, float alpha)
        {
            var tf = parent.Find("Dim");
            Image img;
            if (tf == null)
            {
                var go = new GameObject("Dim", typeof(RectTransform));
                go.transform.SetParent(parent, false);
                img = go.AddComponent<Image>();
            }
            else
            {
                img = tf.GetComponent<Image>() ?? tf.gameObject.AddComponent<Image>();
            }
            img.color = new Color(0f, 0f, 0f, alpha);
            img.raycastTarget = false;
            var rt = img.rectTransform;
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
            rt.SetAsFirstSibling();          // 他のUIより奥に描く
        }

        static void RemoveChild(Transform parent, string name)
        {
            var tf = parent.Find(name);
            if (tf != null) UnityEngine.Object.DestroyImmediate(tf.gameObject);
        }

        static void SetCentered(RectTransform rt, Vector2 offset, Vector2 size)
        {
            rt.anchorMin = new Vector2(0.5f, 0.5f);
            rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = offset;
            rt.sizeDelta = size;
        }

        /// <summary>スプライトの縦横比を保ったまま、指定幅で中央に配置する。</summary>
        static void SetCentered(RectTransform rt, Vector2 offset, float width, Sprite sprite)
        {
            float aspect = (sprite != null && sprite.rect.height > 0f)
                ? sprite.rect.width / sprite.rect.height
                : 1f;
            SetCentered(rt, offset, new Vector2(width, width / aspect));
        }

        /// <summary>
        /// 縦横比を保ったまま、指定した枠に収まる最大の大きさで中央に配置する。
        /// 画像の比率が変わっても他のUIとぶつからないようにするため。
        /// </summary>
        static void SetFitted(RectTransform rt, Vector2 offset, float maxWidth, float maxHeight,
                              Sprite sprite)
        {
            float aspect = (sprite != null && sprite.rect.height > 0f)
                ? sprite.rect.width / sprite.rect.height
                : 1f;

            float width = maxWidth;
            float height = width / aspect;
            if (height > maxHeight)
            {
                height = maxHeight;
                width = height * aspect;
            }
            SetCentered(rt, offset, new Vector2(width, height));
        }

        static TMP_Text FindOrCreateText(Transform parent, string name)
        {
            var tf = parent.Find(name);
            if (tf != null)
            {
                var existing = tf.GetComponent<TMP_Text>();
                if (existing != null) return existing;
                UnityEngine.Object.DestroyImmediate(tf.gameObject);
            }

            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            return go.AddComponent<TextMeshProUGUI>();
        }

        static Image FindOrCreateImage(Transform parent, string name, string spritePath,
                                       Vector4? border = null)
        {
            var tf = parent.Find(name);
            Image img;
            if (tf != null)
            {
                img = tf.GetComponent<Image>();
                if (img == null)
                {
                    UnityEngine.Object.DestroyImmediate(tf.gameObject);
                    tf = null;
                }
            }
            if (tf == null)
            {
                var go = new GameObject(name, typeof(RectTransform));
                go.transform.SetParent(parent, false);
                img = go.AddComponent<Image>();
            }
            else
            {
                img = tf.GetComponent<Image>();
            }

            img.sprite = LoadSprite(spritePath, border);
            img.preserveAspect = true;
            img.raycastTarget = false;
            return img;
        }

        // ---------------- スプライト読み込み ----------------

        /// <summary>
        /// 1枚絵として使えるようインポート設定を整えてから Sprite を読み込む。
        /// 既定では Multiple（自動スライス）になっていて1枚絵として取得できないため。
        /// </summary>
        static Sprite LoadSprite(string path, Vector4? border = null)
        {
            FixSpriteImporter(path, border);

            var sprite = AssetDatabase.LoadAssetAtPath<Sprite>(path);
            if (sprite == null) Debug.LogWarning($"[PuyoGame] スプライトを読み込めませんでした: {path}");
            return sprite;
        }

        /// <summary>1枚絵として使えるようインポート設定を整える。変更したら true。</summary>
        static bool FixSpriteImporter(string path, Vector4? border, bool fullRect = false)
        {
            if (!(AssetImporter.GetAtPath(path) is TextureImporter importer)) return false;

            bool changed = false;

            // タイトメッシュだと輪郭が多角形になって粒子が欠けるので、コマ画像は矩形にする
            if (fullRect)
            {
                var settings = new TextureImporterSettings();
                importer.ReadTextureSettings(settings);
                if (settings.spriteMeshType != SpriteMeshType.FullRect)
                {
                    settings.spriteMeshType = SpriteMeshType.FullRect;
                    importer.SetTextureSettings(settings);
                    changed = true;
                }
            }

            if (importer.textureType != TextureImporterType.Sprite)
            { importer.textureType = TextureImporterType.Sprite; changed = true; }
            if (importer.spriteImportMode != SpriteImportMode.Single)
            { importer.spriteImportMode = SpriteImportMode.Single; changed = true; }
            if (!importer.alphaIsTransparency)
            { importer.alphaIsTransparency = true; changed = true; }
            if (importer.mipmapEnabled)
            { importer.mipmapEnabled = false; changed = true; }
            if (border.HasValue && importer.spriteBorder != border.Value)
            { importer.spriteBorder = border.Value; changed = true; }

            // 元画像が上限より大きいときだけ引き上げる（小さい画像は既定のままでよい）
            importer.GetSourceTextureWidthAndHeight(out int w, out int h);
            int need = Mathf.Max(w, h);
            if (importer.maxTextureSize < need)
            { importer.maxTextureSize = Mathf.Min(Mathf.NextPowerOfTwo(need), 8192); changed = true; }

            if (changed) importer.SaveAndReimport();
            return changed;
        }

        // ---------------- カメラ ----------------

        static void FitMainCamera(BoardView view)
        {
            var cam = Camera.main;
            if (cam == null)
            {
                Debug.LogWarning("[PuyoGame] MainCamera タグのカメラが見つからないので、カメラ調整をスキップしました。");
                return;
            }

            Undo.RecordObject(cam, "Fit Camera To Board");
            Undo.RecordObject(cam.transform, "Fit Camera To Board");

            cam.orthographic = true;
            cam.transform.position = new Vector3(
                view.transform.position.x,
                view.transform.position.y,
                cam.transform.position.z != 0f ? cam.transform.position.z : -10f);

            // 盤面に合わせて画角を決める（盤面が先、カメラが後）。
            // 実行中も画面比率に追従させるため、カメラ側にも同じ計算を持たせる。
            cam.orthographicSize = ComputeOrthographicSize();

            var fitter = cam.GetComponent<BoardCameraFitter>();
            if (fitter == null) fitter = Undo.AddComponent<BoardCameraFitter>(cam.gameObject);
            var fso = new SerializedObject(fitter);
            fso.FindProperty("board").objectReferenceValue = view;
            fso.FindProperty("widthFill").floatValue = WidthFill;
            fso.FindProperty("heightFill").floatValue = HeightFill;
            fso.FindProperty("bottomReserve").floatValue = BottomReserve;
            fso.ApplyModifiedPropertiesWithoutUndo();
        }
    }
}
