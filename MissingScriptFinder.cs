using System.Linq;
using UnityEditor;
using UnityEngine;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using System.IO;
using System;

public static class MissingScriptFinder 
{
    private const string MENU_ROOT = "Tools/Missing References/";
    public static string GetHierarchyName(Transform t)
    {
        if (t == null)
            return "";

        var pname =  GetHierarchyName(t.parent);
        if (pname != "")
        {
            return pname + "/" + t.gameObject.name;
        }
        return t.gameObject.name;
    }

    public static TSource Fold<TSource>(this IEnumerable<TSource> source, Func<TSource, TSource, TSource> func,TSource id)
    {
        TSource r = id;
        foreach(var s in source)
        {
            r = func(r,s);
        }
        return r;
    }
    public static void ForEachI<T>(this IEnumerable<T> source, Action<T,int> action)
    {
        int i = 0;
        foreach (T element in source)
        {
            action(element,i);
            i += 1;
        }
            
    }
    public static void ForEach<T>(this IEnumerable<T> source, Action<T> action)
    {
        foreach (T element in source)
        {
            action(element);
        }
            
    }

    static HashSet<string> findAllScriptUUIDsInAssets()
    {
        var uuids = Directory.GetFiles("Assets/", "*.cs.meta", SearchOption.AllDirectories)
            .Select(p =>
            {
                return File.ReadAllLines(p)[1].Substring(6);
            }).ToList();
        //find dll uuids
        var dlluuids = Directory.GetFiles(EditorApplication.applicationContentsPath, "*.dll", SearchOption.AllDirectories)
        .Select(p =>
        {
            return AssetDatabase.AssetPathToGUID(p.Replace('\\', '/'));
        }).Where(s => s!= "").ToList();
        return new HashSet<string>(uuids.Concat(dlluuids));
    }
    static Regex s_scriptUUIDReg = new Regex(@"m_Script: \{fileID: [0-9]+, guid: ([0-9a-f]{32}), type: 3\}");
    static string getScriptUUID(string line)
    {
        var m = s_scriptUUIDReg.Match(line);
        if (m.Success)
        {
            return m.Groups[1].Value;
        }
        if(line.Contains("m_Script: {fileID: 0}")) //missing script
        {
            return "0";
        }
        return null;
    }
    static Dictionary<string,HashSet<string>> findAllPrefabScriptRefInDir(string dir,Action<int> onBeginFinding,Action<int,string,int> onFinding, Action onEndFinding )
    {
        var allPrefabs = Directory.GetFiles(dir, "*.prefab", SearchOption.AllDirectories);
        onBeginFinding(allPrefabs.Length);
        Dictionary<string, HashSet<string>> r = new Dictionary<string, HashSet<string>>();
        
        for (int i =0;i<allPrefabs.Length;++i)
        {
            onFinding(i, allPrefabs[i],allPrefabs.Length);
            File.ReadAllLines(allPrefabs[i]).ForEach(line =>
            {
                string s = getScriptUUID(line);
                if (s != null)
                {
                    HashSet<string> files = null;
                    r.TryGetValue(s, out files);
                    if (files == null)
                    {
                        files = new HashSet<string>();
                        r.Add(s, files);
                    }
                    files.Add(allPrefabs[i]);
                }
            });
        }
        onEndFinding();
        return r;
    }


    private static void FindMissionRefInGo(GameObject go)
    {
        var components = go.GetComponents<MonoBehaviour>();
        foreach (var c in components)
        {
            // Missing components will be null, we can't find their type, etc.
            if (!c)
            {
                var assetPath =  AssetDatabase.GetAssetPath(go);
                if(assetPath != "" && assetPath != null)
                {
                    Debug.LogError("missing script: " + GetHierarchyName(go.transform) + "-->" + assetPath);
                }
                else
                {
                    Debug.LogError("missing script: " + GetHierarchyName(go.transform));
                }
                continue;
            }
        }
        foreach(Transform t in go.transform)
        {
            FindMissionRefInGo(t.gameObject);
        }
    }
    public static IEnumerable<GameObject> SceneRoots()
    {
        var prop = new HierarchyProperty(HierarchyType.GameObjects);
        var expanded = new int[0];
        while (prop.Next(expanded))
        {
            yield return prop.pptrValue as GameObject;
        }
    }
    [MenuItem(MENU_ROOT + "FindIn open Scene")]
    public static void FindMissingReferencesInCurrentScene()
    {
        var objs = SceneRoots();
        int count = objs.Count();
        objs.ForEachI((prefab, i) =>
        {
            EditorUtility.DisplayProgressBar("check missing prefabs", prefab.ToString(), (float)i / count);
            FindMissionRefInGo(prefab);
        });
        EditorUtility.ClearProgressBar();
    }

    [MenuItem(MENU_ROOT + "FindInAssets")]
    public static void MissingSpritesInAssets()
    {
        var allScriptsIds = findAllScriptUUIDsInAssets();
        var refScriptIds = findAllPrefabScriptRefInDir("Assets/",
        (count) =>
        {
            EditorUtility.DisplayProgressBar("scanning","",0);
        },
        (idx,file,count) =>
        {
            EditorUtility.DisplayProgressBar("scanning", file, (float) idx/count);
        },
        () =>
        {
            EditorUtility.ClearProgressBar();
        });
        var missingScriptsFiles = refScriptIds
        .Where(kv => !allScriptsIds.Contains(kv.Key))
        .Select(kv => kv.Value)
        .ToList()
        .Fold((a,b)=>new HashSet<string>(a.Concat(b)),new HashSet<string>());
        Debug.LogError("----------------------------------------->\nMissingFiles: "  + missingScriptsFiles.Count);
        missingScriptsFiles.ForEachI((f, i) =>
        {
            EditorUtility.DisplayProgressBar("check missing prefabs", f, (float)i / missingScriptsFiles.Count);
            var prefab = AssetDatabase.LoadAssetAtPath(f, typeof(GameObject)) as GameObject;
            FindMissionRefInGo(prefab);
        });
        EditorUtility.ClearProgressBar();
    }

}
