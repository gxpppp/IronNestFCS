using System.Collections;
using Il2Cpp;
using Il2CppTMPro;
using IronNestFCS.Logic.FCS;
using MelonLoader;
using UnityEngine;
using Object = UnityEngine.Object;

namespace IronNestFCS.Logic;

public class FcsSceneInteractor {
    private FSC fcs;

    private List<GameObject> destroyOnShutdown = new();
    private readonly ClickRaycaster clicks = new();

    // 当前选中的弹种（两管炮共享，由调度器决定任务派到哪管炮）。
    public BulletType selectedBulletType = BulletType.HE;

    private List<GameObject> bulletTypeBtns = new();

    // 每个地图目标对应一个按钮：targetId -> 按钮。点击=用当前弹种为该目标入队一个任务。
    private readonly Dictionary<int, GameObject> targetButtons = new();
    // 该目标当前有没有未完成任务在跑（避免重复入队）。
    private readonly HashSet<int> activeTargets = new();

    public bool AutoFire = false;

    public FcsSceneInteractor(FSC fcs) {
        this.fcs = fcs;
    }

    public void Initialize() {
        InitializeBulletTypeButtons();
        InitializeTargetButtons();
    }

    private void InitializeBulletTypeButtons() {
        const float z = -18.4181f;
        float x = 0.3488f;
        foreach (BulletType type in Enum.GetValues(typeof(BulletType))) {
            BulletType captured = type;
            // 先声明再赋值：lambda 要捕获 button，不能在其声明表达式内部引用它。
            GameObject button = null;
            button = AddButton(() => {
                selectedBulletType = captured;
                foreach (var btn in bulletTypeBtns) {
                    SetColor(btn, btn == button ? Color.green : Color.white);
                }
            }, type == BulletType.HE ? Color.green : Color.white);
            button.transform.position = new Vector3(x, -0.6916f, z);
            button.transform.localScale = Vector3.one * 0.02f;
            bulletTypeBtns.Add(button);
            var text = AddText(type.ToString(), 14f);
            text.transform.SetParent(button.transform, false);
            text.transform.localPosition = new Vector3(-1.9f, 0, -10.6f);
            text.transform.localScale = Vector3.one * 1.0f;
            x -= 0.05f;
        }

        GameObject autoFireButton = null;
        autoFireButton = AddButton(() => {
            AutoFire = !AutoFire;
            SetColor(autoFireButton, AutoFire ? Color.red : Color.white);
        }, AutoFire ? Color.red : Color.white);
        autoFireButton.transform.position = new Vector3(x, -0.6916f, z);
        autoFireButton.transform.localScale = Vector3.one * 0.02f;
        var autoFiretext = AddText("Auto Fire", 14f);
        autoFiretext.transform.SetParent(autoFireButton.transform, false);
        autoFiretext.transform.localPosition = new Vector3(-1.9f, 0, -10.6f);
        autoFiretext.transform.localScale = Vector3.one * 1.0f;
    }

    /// <summary>
    /// 4 个目标按钮（对应地图上 1~4 号炮兵标记）。点击即用当前选中弹种为该目标入队一个任务，
    /// 调度器自动派给空闲炮管。用 activeTargets 防止同一目标重复入队。
    /// </summary>
    private void InitializeTargetButtons() {
        const float z = -18.6381f;
        float x = 0.3488f;
        for (int i = 1; i <= 4; i++) {
            int targetId = i;
            GameObject button = AddButton(() => {
                if (activeTargets.Contains(targetId)) {
                    return; // 该目标已有任务在跑，忽略重复点击
                }
                var task = fcs.MapTable.GetMarkTarget(targetId);
                if (task == null) {
                    return; // 地图上没有这个编号的目标
                }
                task.targetId = targetId;
                task.bulletType = selectedBulletType;
                activeTargets.Add(targetId);
                SetColor(targetButtons[targetId], Color.gray);
                fcs.EnqueueTask(task);
            }, Color.red);
            button.transform.position = new Vector3(x, -0.6916f, z);
            button.transform.localScale = Vector3.one * 0.02f;
            targetButtons[targetId] = button;
            var text = AddText("T" + targetId, 14f);
            text.transform.SetParent(button.transform, false);
            text.transform.localPosition = new Vector3(-1.9f, 0, -10.6f);
            text.transform.localScale = Vector3.one * 1.0f;
            x -= 0.05f;
        }
    }

    /// <summary>任务完成回调：把对应目标按钮变红，并解除 active 标记以便再次下达。</summary>
    public void TaskFinished(ArtilleryTask task) {
        activeTargets.Remove(task.targetId);
        if (targetButtons.TryGetValue(task.targetId, out var button)) {
            SetColor(button, Color.red);
        }
    }
    
    public void Update() {
        clicks.Update();
    }

    public void ShutDown() {
        clicks.Clear();
        foreach (var obj in destroyOnShutdown) {
            Object.Destroy(obj);
        }
    }
    
    public GameObject AddButton(Action onClick) {
        return AddButton(onClick, Color.white);
    }

    public GameObject AddButton(Action onClick, Color color) {
        // 用自带 BoxCollider 的 cube 当可点击目标，靠 ClickRaycaster 自己 raycast 检测点击，
        // 不依赖游戏的 LookAtTarget，也不注册新 IL2CPP 类型（保持可热重载）。
        var button = GameObject.CreatePrimitive(PrimitiveType.Cube);
        destroyOnShutdown.Add(button);
        var collider = button.GetComponent<Collider>();
        clicks.Register(collider, onClick);
        SetColor(button, color);
        return button;
    }

    /// <summary>
    /// 给对象的 Renderer 换上当前渲染管线（URP）的材质并设颜色。
    /// CreatePrimitive 默认用内置管线的 Standard 材质，在 URP 下 shader 无效会渲染成紫色；
    /// 这里用 URP 的 Unlit shader 重建材质（不受光照影响，纯色所见即所得）。
    /// </summary>
    public static void SetColor(GameObject go, Color color) {
        var renderer = go.GetComponent<Renderer>();
        if (renderer == null)
            return;

        var shader = Shader.Find("Universal Render Pipeline/Unlit")
                     ?? Shader.Find("Universal Render Pipeline/Lit");
        if (shader == null) {
            MelonLogger.Warning("[FCS] 未找到 URP shader，颜色可能不正确。");
            // 退而求其次：直接改现有材质颜色
            if (renderer.material != null)
                renderer.material.color = color;
            return;
        }

        var mat = new Material(shader);
        // URP Unlit 用 _BaseColor 控制颜色；同时设 color 兼容。
        mat.color = color;
        if (mat.HasProperty("_BaseColor"))
            mat.SetColor("_BaseColor", color);
        renderer.material = mat;
    }

    /// <summary>
    /// 在 3D 世界里创建一段文本（World Space 的 TextMeshPro，非 UGUI）。
    /// 返回 GameObject，调用方自行设 transform.position/scale。文本/字号后续可通过
    /// go.GetComponent&lt;TextMeshPro&gt;() 修改。英文数字用默认字体即可显示。
    /// </summary>
    public GameObject AddText(string text, float fontSize = 4f) {
        var go = new GameObject("FcsText");
        destroyOnShutdown.Add(go);
        go.transform.Rotate(new Vector3(90, 0, 0));
        go.transform.Rotate(new Vector3(0, 0, -90));
        var tmp = go.AddComponent<TextMeshPro>();
        // AddComponent 后 Awake 未必已执行，字体可能未自动赋值导致不渲染；
        // 显式赋默认字体（含 ASCII，英文数字足够）。
        if (tmp.font == null && TMP_Settings.defaultFontAsset != null)
            tmp.font = TMP_Settings.defaultFontAsset;
        tmp.text = text;
        tmp.fontSize = fontSize;
        tmp.color = Color.white;
        // 锚点设到左上角，方便从左上往下排版（Center 会以几何中心为原点）。
        // tmp.alignment = TextAlignmentOptions.MidlineLeft;
        return go;
    }
    
    public static IEnumerator WaitAndClick(LookAtTarget button) {
        while (button.isActive == false || button.nextAllowedClickTime > Time.realtimeSinceStartup) {
            yield return new WaitForSeconds(0.2f);
        }
        yield return new WaitForSeconds(0.2f);
        button.OnClickDown();
        yield return new WaitForSeconds(0.1f);
        button.OnClickUp();
    }
}