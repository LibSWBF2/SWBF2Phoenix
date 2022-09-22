using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
using UnityEditor.IMGUI.Controls;

public class PhxLuaEditor : EditorWindow
{
    // every X seconds, save the edited lua code
    const float SAVE_INTERVALL = 2.0f;
    const string SAVE_CODE_KEY = "SWBF2RuntimeLuaEditorCode";
    float SaveCounter = 0;
    
    string LuaCode;
    GUIStyle EditorStyle;

    TableTreeView TreeView;


    [MenuItem("Phoenix/Lua Editor")]
    public static void OpenLuaEditor()
    {
        PhxLuaEditor window = GetWindow<PhxLuaEditor>();
        window.Show();
    }

    void Awake()
    {
        LuaCode = PlayerPrefs.GetString(SAVE_CODE_KEY);
        EditorStyle = new GUIStyle();
        EditorStyle.fontSize = 28;
        EditorStyle.fontStyle = FontStyle.Normal;
        EditorStyle.normal.textColor = Color.white;
    }

    void Update()
    {
        SaveCounter += Time.deltaTime;
        if (SaveCounter >= SAVE_INTERVALL)
        {
            PlayerPrefs.SetString(SAVE_CODE_KEY, LuaCode);
            SaveCounter = 0.0f;
        }

        Repaint();
    }

    void OnGUI()
    {
        PhxLuaRuntime runtime = PhxGame.GetLuaRuntime();
        if (!Application.isPlaying || runtime == null)
        {
            EditorGUILayout.LabelField("LUA is not running");
            return;
        }
        Lua L = runtime.GetLua();

        GUILayout.BeginHorizontal();
        LuaCode = GUILayout.TextArea(LuaCode, /*EditorStyle, */GUILayout.Width(400), GUILayout.ExpandHeight(true));

        GUILayout.BeginVertical(GUILayout.ExpandWidth(true));
        int stackSize = L.GetTop();
        for (int i = stackSize - 1; i >= 0; --i)
        {
            Lua.ValueType type = L.Type(i);
            string typeStr = type.ToString();
            if (type == Lua.ValueType.TABLE)
            {
                GUILayout.BeginHorizontal();
                EditorGUILayout.PrefixLabel("TABLE");
                if (GUILayout.Button(TreeView == null ? "Expand" : "Collapse"))
                {
                    if (TreeView == null)
                    {
                        TreeView = new TableTreeView(new TreeViewState(), i);
                    }
                    else
                    {
                        TreeView = null;
                    }
                }
                Rect last = GUILayoutUtility.GetLastRect();
                GUILayout.EndHorizontal();
                if (TreeView != null && TreeView.TableIdx == i)
                {
                    GUILayout.Space(100);
                    TreeView.OnGUI(new Rect(last.x, last.y + last.height, position.width, 100));
                }
            }
            else
            {
                object value = runtime.ToValue(i);
                string valueStr = value != null ? value.ToString() : "NIL";
                EditorGUILayout.LabelField(typeStr, valueStr);
            }
        }

        GUILayout.Space(20);
        EditorGUILayout.LabelField("GC Count", L.GetGCCount().ToString());
        EditorGUILayout.LabelField("GC Threshold", L.GetGCThreshold().ToString());
        EditorGUILayout.LabelField("Used callbacks", L.UsedCallbackCount.ToString() + "/" + Lua.CallbackMapSize);
        GUILayout.EndVertical();
        GUILayout.EndHorizontal();
        
        if (GUILayout.Button("Execute"))
        {
            runtime.ExecuteString(LuaCode);
        }
    }
}

class TableTreeView : TreeView
{
    public int TableIdx { get; private set; }

    public TableTreeView(TreeViewState treeViewState, int tableIdx)
        : base(treeViewState)
    {
        TableIdx = tableIdx;
        Reload();
    }

    protected override TreeViewItem BuildRoot()
    {
        var root = new TreeViewItem { id = 0, depth = -1, displayName = "TABLE" };

        PhxLuaRuntime runtime = PhxGame.GetLuaRuntime();
        if (!Application.isPlaying || runtime == null)
        {
            return root;
        }
        Lua L = runtime.GetLua();

        // BuildRoot is called every time Reload is called to ensure that TreeViewItems 
        // are created from data. Here we create a fixed set of items. In a real world example,
        // a data model should be passed into the TreeView and the items created from the model.

        // This section illustrates that IDs should be unique. The root item is required to 
        // have a depth of -1, and the rest of the items increment from that.

        PhxLuaRuntime.Table table = runtime.ToValue(TableIdx) as PhxLuaRuntime.Table;
        if (table != null)
        {
            var allItems = new List<TreeViewItem>();
            int id = 0;

            void AddTable(PhxLuaRuntime.Table t, int depth)
            {
                foreach (KeyValuePair<object, object> entry in table)
                {
                    if (entry.Value is PhxLuaRuntime.Table)
                    {
                        AddTable((PhxLuaRuntime.Table)entry.Value, depth + 1);
                    }
                    else
                    {
                        allItems.Add(new TreeViewItem 
                        { 
                            id = id++, 
                            depth = depth, 
                            displayName = string.Format($"{entry.Key.ToString()} = {entry.Value.ToString()}") 
                        });
                    }
                }
            }

            AddTable(table, 0);

            // Utility method that initializes the TreeViewItem.children and .parent for all items.
            SetupParentsAndChildrenFromDepths(root, allItems);
        }


        // Return root of the tree
        return root;
    }
}