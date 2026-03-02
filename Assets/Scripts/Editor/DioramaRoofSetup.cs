using System.IO;
using UnityEditor;
using UnityEngine;

/// <summary>ダイオラマ屋根用アセット（テクスチャ・マテリアル）を生成・更新する</summary>
public static class DioramaRoofSetup
{
    const string TexPath = "Assets/Textures";
    const string MatPath = "Assets/Materials";
    const string SnowNoisePath = TexPath + "/SnowNoise.png";
    const string SnowNormalPath = TexPath + "/SnowNormal.png";
    const string RoofWoodMatPath = MatPath + "/DioramaRoofWood.mat";
    const string RoofSnowMatPath = MatPath + "/DioramaRoofSnow.mat";

    [MenuItem("SnowPanicVibe/Create Diorama Roof Assets")]
    public static void CreateAllAssets()
    {
        EnsureDirectories();
        CreateSnowNoiseTexture();
        CreateSnowNormalTexture();
        AssetDatabase.Refresh();
        ConfigureSnowNormalImport();
        CreateRoofMaterials();
        AssetDatabase.Refresh();
        Debug.Log("Diorama Roof assets created. Run Setup Cornice Scene to apply.");
    }

    static void EnsureDirectories()
    {
        if (!AssetDatabase.IsValidFolder("Assets/Materials"))
            AssetDatabase.CreateFolder("Assets", "Materials");
        if (!Directory.Exists(Path.Combine(Application.dataPath, "Textures")))
            Directory.CreateDirectory(Path.Combine(Application.dataPath, "Textures"));
    }

    static void CreateSnowNoiseTexture()
    {
        int size = 64;
        var tex = new Texture2D(size, size, TextureFormat.RGBA32, true);
        tex.filterMode = FilterMode.Bilinear;
        tex.wrapMode = TextureWrapMode.Repeat;
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float nx = x / (float)size * 4f;
                float ny = y / (float)size * 4f;
                float n = Mathf.PerlinNoise(nx, ny) * 0.12f + 0.88f;
                tex.SetPixel(x, y, new Color(n, n * 0.99f, n * 1.02f, 1f));
            }
        }
        tex.Apply();
        var bytes = tex.EncodeToPNG();
        Object.DestroyImmediate(tex);
        File.WriteAllBytes(Path.Combine(Application.dataPath, "Textures/SnowNoise.png"), bytes);
        Debug.Log("Created SnowNoise.png");
    }

    /// <summary>グレースケールの高さマップ。インポーターで Convert to Normal Map を使用</summary>
    static void CreateSnowNormalTexture()
    {
        int size = 64;
        var tex = new Texture2D(size, size, TextureFormat.RGBA32, true);
        tex.filterMode = FilterMode.Bilinear;
        tex.wrapMode = TextureWrapMode.Repeat;
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float nx = x / (float)size * 5f;
                float ny = y / (float)size * 5f;
                float h = Mathf.PerlinNoise(nx, ny) * 0.4f + Mathf.PerlinNoise(nx * 2f, ny * 2f) * 0.2f + 0.4f;
                tex.SetPixel(x, y, new Color(h, h, h, 1f));
            }
        }
        tex.Apply();
        var bytes = tex.EncodeToPNG();
        Object.DestroyImmediate(tex);
        File.WriteAllBytes(Path.Combine(Application.dataPath, "Textures/SnowNormal.png"), bytes);
        Debug.Log("Created SnowNormal.png");
    }

    static void ConfigureSnowNormalImport()
    {
        var importer = AssetImporter.GetAtPath(SnowNormalPath) as TextureImporter;
        if (importer == null) return;
        importer.textureType = TextureImporterType.NormalMap;
        importer.SaveAndReimport();
    }

    static void CreateRoofMaterials()
    {
        var litShader = Shader.Find("Universal Render Pipeline/Lit");
        if (litShader == null) litShader = Shader.Find("Standard");

        var wood = new Material(litShader);
        wood.name = "DioramaRoofWood";
        wood.SetColor("_BaseColor", new Color(0.68f, 0.48f, 0.38f));
        wood.SetFloat("_Smoothness", 0.25f);
        wood.SetFloat("_Metallic", 0f);
        AssetDatabase.CreateAsset(wood, RoofWoodMatPath);
        Debug.Log("Created DioramaRoofWood.mat");

        var snow = new Material(litShader);
        snow.name = "DioramaRoofSnow";
        snow.SetColor("_BaseColor", new Color(0.82f, 0.86f, 0.9f));
        snow.SetFloat("_Smoothness", 0.35f);
        snow.SetFloat("_Metallic", 0f);
        var noiseTex = AssetDatabase.LoadAssetAtPath<Texture2D>(SnowNoisePath);
        var normalTex = AssetDatabase.LoadAssetAtPath<Texture2D>(SnowNormalPath);
        if (noiseTex != null) snow.SetTexture("_BaseMap", noiseTex);
        if (normalTex != null)
        {
            snow.SetTexture("_BumpMap", normalTex);
            snow.SetFloat("_BumpScale", 0.4f);
            snow.EnableKeyword("_NORMALMAP");
        }
        AssetDatabase.CreateAsset(snow, RoofSnowMatPath);
        Debug.Log("Created DioramaRoofSnow.mat");
    }
}
