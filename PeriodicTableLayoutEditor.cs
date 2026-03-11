using UnityEngine;
using UnityEditor;
using System.Collections.Generic;

public class PeriodicTableLayoutEditor : EditorWindow
{
    // 物体ID → (行, 列)，行列从0开始，共18列
    // 第7行=镧系，第8行=锕系（含一行视觉间隔，totalRows=10）
    private static readonly Dictionary<int, (int row, int col)> Layout = new Dictionary<int, (int, int)>
    {
        // Period 1
        {20004,(0,0)},  {20016,(0,17)}, // H, He
        // Period 2
        {20017,(1,0)},  {20018,(1,1)},                                                                          // Li, Be
        {20019,(1,12)}, {20001,(1,13)}, {20020,(1,14)}, {20003,(1,15)}, {20021,(1,16)}, {20022,(1,17)}, // B C N O F Ne
        // Period 3
        {20005,(2,0)},  {20023,(2,1)},                                                                          // Na, Mg
        {20024,(2,12)}, {20025,(2,13)}, {20026,(2,14)}, {20027,(2,15)}, {20028,(2,16)}, {20029,(2,17)}, // Al Si P S Cl Ar
        // Period 4
        {20030,(3,0)},  {20031,(3,1)},  {20032,(3,2)},  {20033,(3,3)},  {20034,(3,4)},  {20035,(3,5)},
        {20036,(3,6)},  {20002,(3,7)},  {20037,(3,8)},  {20038,(3,9)},  {20039,(3,10)}, {20040,(3,11)},
        {20041,(3,12)}, {20042,(3,13)}, {20043,(3,14)}, {20044,(3,15)}, {20045,(3,16)}, {20046,(3,17)},
        // Period 5
        {20047,(4,0)},  {20048,(4,1)},  {20049,(4,2)},  {20050,(4,3)},  {20051,(4,4)},  {20052,(4,5)},
        {20053,(4,6)},  {20054,(4,7)},  {20055,(4,8)},  {20056,(4,9)},  {20006,(4,10)}, {20057,(4,11)},
        {20058,(4,12)}, {20059,(4,13)}, {20060,(4,14)}, {20061,(4,15)}, {20062,(4,16)}, {20063,(4,17)},
        // Period 6（col2留空给镧系占位符，La放第7行）
        {20064,(5,0)},  {20065,(5,1)},
        {20081,(5,3)},  {20082,(5,4)},  {20083,(5,5)},  {20084,(5,6)},  {20085,(5,7)},  {20086,(5,8)},
        {20087,(5,9)},  {20007,(5,10)}, {20088,(5,11)}, {20089,(5,12)}, {20090,(5,13)}, {20091,(5,14)},
        {20092,(5,15)}, {20093,(5,16)}, {20094,(5,17)},
        // Period 7（col2留空给锕系占位符，Ac放第8行）
        {20095,(6,0)},  {20096,(6,1)},
        {20112,(6,3)},  {20113,(6,4)},  {20114,(6,5)},  {20115,(6,6)},  {20116,(6,7)},  {20117,(6,8)},
        {20118,(6,9)},  {20119,(6,10)}, {20120,(6,11)}, {20121,(6,12)}, {20122,(6,13)}, {20123,(6,14)},
        {20124,(6,15)}, {20125,(6,16)}, {20126,(6,17)},
        // 镧系 row=7（La~Lu，col 2~16）
        {20066,(7,2)},  {20067,(7,3)},  {20068,(7,4)},  {20069,(7,5)},  {20070,(7,6)},  {20071,(7,7)},
        {20072,(7,8)},  {20073,(7,9)},  {20074,(7,10)}, {20075,(7,11)}, {20076,(7,12)}, {20077,(7,13)},
        {20078,(7,14)}, {20079,(7,15)}, {20080,(7,16)},
        // 锕系 row=8（Ac~Lr，col 2~16）
        {20097,(8,2)},  {20098,(8,3)},  {20099,(8,4)},  {20100,(8,5)},  {20101,(8,6)},  {20102,(8,7)},
        {20103,(8,8)},  {20104,(8,9)},  {20105,(8,10)}, {20106,(8,11)}, {20107,(8,12)}, {20108,(8,13)},
        {20109,(8,14)}, {20110,(8,15)}, {20111,(8,16)},
    };

    // ========== 参数 ==========
    private const int TOTAL_COLS = 18;
    private const int TOTAL_ROWS = 9;    // 主表7行 + 镧系/锕系各1行
    private const float LANTHANIDE_GAP = 0.01f; // 镧系锕系与主表之间的额外间距比例

    private float gapX = 0.003f;
    private float gapY = 0.003f;
    private float paddingX = 0.005f;
    private float paddingY = 0.005f;

    private GameObject contentRoot;

    [MenuItem("Tools/Periodic Table Layout")]
    public static void ShowWindow() => GetWindow<PeriodicTableLayoutEditor>("元素周期表布局");

    private void OnGUI()
    {
        GUILayout.Label("元素周期表自动布局", EditorStyles.boldLabel);
        contentRoot = (GameObject)EditorGUILayout.ObjectField("Content 根节点", contentRoot, typeof(GameObject), true);
        EditorGUILayout.Space();
        gapX = EditorGUILayout.Slider("列间隙", gapX, 0f, 0.01f);
        gapY = EditorGUILayout.Slider("行间隙", gapY, 0f, 0.01f);
        paddingX = EditorGUILayout.Slider("左右边距", paddingX, 0f, 0.05f);
        paddingY = EditorGUILayout.Slider("上下边距", paddingY, 0f, 0.05f);

        EditorGUILayout.Space();
        GUI.enabled = contentRoot != null;
        if (GUILayout.Button("▶ 执行布局", GUILayout.Height(32))) ApplyLayout();
        GUI.enabled = true;
    }

    private void ApplyLayout()
    {
        float usableW = 1f - paddingX * 2f;
        // 高度：TOTAL_ROWS行 + 1个额外间隙（镧系/锕系分隔）
        float usableH = 1f - paddingY * 2f - LANTHANIDE_GAP;
        float cellW = (usableW - gapX * (TOTAL_COLS - 1)) / TOTAL_COLS;
        float cellH = (usableH - gapY * (TOTAL_ROWS - 1)) / TOTAL_ROWS;

        int ok = 0, skip = 0;
        Undo.SetCurrentGroupName("Periodic Table Layout");

        foreach (Transform child in contentRoot.transform)
        {
            if (!int.TryParse(child.name, out int id) || !Layout.TryGetValue(id, out var pos))
            { skip++; continue; }

            RectTransform rt = child.GetComponent<RectTransform>();
            if (rt == null) { skip++; continue; }

            Undo.RecordObject(rt, "Periodic Table Layout");

            float minX = paddingX + pos.col * (cellW + gapX);
            float maxX = minX + cellW;

            // row >= 7 时加入额外间距（镧系/锕系分隔线）
            float extraGap = pos.row >= 7 ? LANTHANIDE_GAP : 0f;
            float maxY = 1f - paddingY - pos.row * (cellH + gapY) - extraGap;
            float minY = maxY - cellH;

            rt.anchorMin = new Vector2(minX, minY);
            rt.anchorMax = new Vector2(maxX, maxY);
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
            ok++;
        }

        Debug.Log($"[周期表布局] 完成 {ok} 个，跳过 {skip} 个");
    }
}