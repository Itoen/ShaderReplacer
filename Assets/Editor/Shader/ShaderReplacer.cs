using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

public class ShaderReplacer : EditorWindow
{
    Vector2 ScrollPosition = Vector2.zero;
    static Shader oldShader;
    static Shader newShader;
    static List<ReplacePropertyName> propertyNames = new List<ReplacePropertyName>();
    static Dictionary<int, PropertyData> oldShaderPropertyData = new Dictionary<int, PropertyData>();
    static Dictionary<int, PropertyData> newShaderPropertyData = new Dictionary<int, PropertyData>();

    class PropertyData
    {
        public ShaderUtil.ShaderPropertyType propertyType;
        public string propertyName;
    }

    class ReplacePropertyName
    {
        public ShaderUtil.ShaderPropertyType propertyType;
        public int oldShaderPropertyIndex;
        public int newShaderPropertyIndex;
        public bool isCustomValue = false;
        public object customValue = null;
    }

    [MenuItem("Assets/Shader/Replace", true)]
    static bool IsEnabled ()
    {
        foreach (var selection in Selection.objects)
        {
            string selectionPath = AssetDatabase.GetAssetPath(selection);
            if (!Directory.Exists(selectionPath))
            {
                return true;
            }
        }

        return false;
    }

    [MenuItem("Assets/Shader/Replace", false, 30)]
    static void Create ()
    {
        GetWindow<ShaderReplacer>();
        propertyNames.Clear();
        oldShaderPropertyData.Clear();
        newShaderPropertyData.Clear();
        newShader = null;
        oldShader = null;

        foreach (var selection in Selection.objects)
        {
            string selectionPath = AssetDatabase.GetAssetPath(selection);
            Shader shader = (Shader)selection;
            if (!Directory.Exists(selectionPath) && shader != null)
            {
                oldShader = shader;
                oldShaderPropertyData = CreatePropertyData(oldShader);
                return;
            }
        }
    }

    static Dictionary<int, PropertyData> CreatePropertyData (Shader shader)
    {
        var count = ShaderUtil.GetPropertyCount(shader);
        var dict = new Dictionary<int, PropertyData>();
        for (var i = 0; i < count; i++)
        {
            var propertyData = new PropertyData();
            propertyData.propertyName = ShaderUtil.GetPropertyName(shader, i);
            propertyData.propertyType = ShaderUtil.GetPropertyType(shader, i);
            dict.Add(i, propertyData);
        }
        return dict;
    }

    static void Replace ()
    {

        var searchGUIDList = AssetDatabase.FindAssets("t:Material ");
        var searchCount = searchGUIDList.Length;
        var oldShaderPath = AssetDatabase.GetAssetPath(oldShader);
        var oldShaderGUID = AssetDatabase.AssetPathToGUID(oldShaderPath);
        for (int i = 0; i < searchGUIDList.Count(); i++)
        {
            var targetGUID = searchGUIDList[i];
            var targetPath = AssetDatabase.GUIDToAssetPath(targetGUID);
            EditorUtility.DisplayProgressBar("置換中", (i + 1).ToString() + "/" + searchCount, i / (float)searchCount);
            foreach (var referentGUID in AssetDatabase.GetDependencies(targetPath).Select(AssetDatabase.AssetPathToGUID))
            {
                if (referentGUID.Equals(targetGUID)) { continue; }

                if (referentGUID.Equals(oldShaderGUID))
                {
                    var material = AssetDatabase.LoadAssetAtPath<Material>(targetPath);
                    if (material == null)
                    {
                        continue;
                    }
                    ReplaceShader(ref material);
                    EditorUtility.SetDirty(material);
                }
            }
        }

        EditorUtility.ClearProgressBar();
        GetWindow<ShaderReplacer>().Repaint();
    }

    static void ReplaceShader (ref Material material)
    {
        var old = Instantiate(material);
        material.shader = newShader;

        foreach (var propertyName in propertyNames)
        {
            var beforePropertyName = oldShaderPropertyData[propertyName.oldShaderPropertyIndex].propertyName;
            var afterPropertyName = newShaderPropertyData[propertyName.newShaderPropertyIndex].propertyName;

            switch (propertyName.propertyType)
            {
                case ShaderUtil.ShaderPropertyType.Float:
                case ShaderUtil.ShaderPropertyType.Range:
                    var floatValue = propertyName.isCustomValue ? (float)propertyName.customValue : old.GetFloat(beforePropertyName);
                    material.SetFloat(afterPropertyName, floatValue);
                    break;

                case ShaderUtil.ShaderPropertyType.TexEnv:
                    var textureValue = propertyName.isCustomValue ? (Texture2D)propertyName.customValue : old.GetTexture(beforePropertyName);
                    var tilingValue = propertyName.isCustomValue ? (Vector4)propertyName.customValue : old.GetVector(string.Format("{0}_ST", beforePropertyName));
                    material.SetTexture(afterPropertyName, textureValue);
                    material.SetVector(string.Format("{0}_ST", afterPropertyName), tilingValue);

                    if (old.HasProperty(string.Format("{0}_TexelSize", beforePropertyName)))
                    {
                        var textureSizeValue = propertyName.isCustomValue ? (Vector4)propertyName.customValue : old.GetVector(string.Format("{0}_TexelSize", beforePropertyName));
                        material.SetVector(string.Format("{0}_TexelSize", afterPropertyName), textureSizeValue);
                    }
                    break;

                case ShaderUtil.ShaderPropertyType.Vector:
                    var vectorValue = propertyName.isCustomValue ? (Vector4)propertyName.customValue : old.GetVector(beforePropertyName);
                    material.SetVector(afterPropertyName, vectorValue);
                    break;

                case ShaderUtil.ShaderPropertyType.Color:
                    var colorValue = propertyName.isCustomValue ? (Color)propertyName.customValue : old.GetColor(beforePropertyName);
                    material.SetColor(afterPropertyName, colorValue);
                    break;

            }
        }
        material.renderQueue = old.renderQueue;
    }

    void OnGUI ()
    {
        this.ScrollPosition = GUILayout.BeginScrollView(this.ScrollPosition);

        EditorGUILayout.LabelField("元のシェーダー");
        EditorGUILayout.BeginVertical("box");
        string oldShaderName = (oldShader != null) ? oldShader.name : string.Empty;
        EditorGUILayout.LabelField(oldShaderName);
        EditorGUILayout.EndVertical();

        GUILayout.Label("置き換えるシェーダ");
        var shader = (Shader)EditorGUILayout.ObjectField(newShader, typeof(Shader), true);
        if (shader != newShader)
        {
            newShader = shader;
            newShaderPropertyData = CreatePropertyData(newShader);
        }

        GUILayout.Label("引き継ぐプロパティ名(上：元の、下：引き継ぎ後)");
        for (var i = 0; i < propertyNames.Count; i++)
        {
            GUILayout.Label(string.Format("{0}", i + 1));

            var newShaderValidPropertyData = newShaderPropertyData;
            if (!propertyNames[i].isCustomValue)
            {
                propertyNames[i].oldShaderPropertyIndex = EditorGUILayout.IntPopup(propertyNames[i].oldShaderPropertyIndex, oldShaderPropertyData.Values.Select(data => data.propertyName).ToArray(), oldShaderPropertyData.Keys.ToArray());
                newShaderValidPropertyData = newShaderValidPropertyData.Where(data =>
                {
                    var propertyType = oldShaderPropertyData[propertyNames[i].oldShaderPropertyIndex].propertyType;
                    if (propertyType == data.Value.propertyType) return true;
                    if (propertyType == ShaderUtil.ShaderPropertyType.Float && data.Value.propertyType == ShaderUtil.ShaderPropertyType.Range) return true;
                    return (propertyType == ShaderUtil.ShaderPropertyType.Range && data.Value.propertyType == ShaderUtil.ShaderPropertyType.Float);
                })
                    .ToDictionary(data => data.Key, data => data.Value);
            }

            propertyNames[i].newShaderPropertyIndex = EditorGUILayout.IntPopup(propertyNames[i].newShaderPropertyIndex, newShaderValidPropertyData.Values.Select(data => data.propertyName).ToArray(), newShaderValidPropertyData.Keys.ToArray());
            propertyNames[i].propertyType = newShaderPropertyData[propertyNames[i].newShaderPropertyIndex].propertyType;

            propertyNames[i].isCustomValue = EditorGUILayout.Toggle("固定値設定", propertyNames[i].isCustomValue);
            if (propertyNames[i].isCustomValue)
            {
                propertyNames[i].customValue = CustomValueField(propertyNames[i].propertyType, propertyNames[i].customValue);
            }

            if (GUILayout.Button("削除", GUILayout.Width(100)))
            {
                propertyNames.RemoveAt(i);
            }
        }

        if (GUILayout.Button("プロパティ追加"))
        {
            propertyNames.Add(new ReplacePropertyName());
        }

        if (GUILayout.Button("置換", GUILayout.Width(100)) && newShader != null)
        {
            Replace();
        }

        GUILayout.EndScrollView();
    }

    object CustomValueField (ShaderUtil.ShaderPropertyType type, object value)
    {
        switch (type)
        {
            case ShaderUtil.ShaderPropertyType.Color:
                if ((Color)value == null || !(value is Color)) value = Color.white;
                return EditorGUILayout.ColorField((Color)value);

            case ShaderUtil.ShaderPropertyType.Float:
            case ShaderUtil.ShaderPropertyType.Range:
                if (value == null || !(value is float)) value = 0f;
                return EditorGUILayout.FloatField((float)value);

            case ShaderUtil.ShaderPropertyType.TexEnv:
                if ((Texture2D)value == null || !(value is Texture2D)) value = Texture2D.whiteTexture;
                return EditorGUILayout.ObjectField((Texture2D)value, typeof(Texture2D), true);

            case ShaderUtil.ShaderPropertyType.Vector:
                if ((Vector4)value == null || !(value is Vector4)) value = Vector4.zero;
                return EditorGUILayout.Vector4Field("", (Vector4)value);
        }

        return null;
    }
}

