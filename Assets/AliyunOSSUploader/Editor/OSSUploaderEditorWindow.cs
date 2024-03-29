using Aliyun.OSS;
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;

public class AliyunConfig : ScriptableObject
{
    public string accessKeyId = "";
    public string accessKeySecret = "";
    public string endpoint = "oss-cn-shanghai.aliyuncs.com";
    public string bucketName = "static-app";
    public string objectKey = ""; //oss存储路径

    public string selectedFolderPath = "";// 本地上传文件夹路径
}

public class OSSUploaderEditorWindow : EditorWindow
{
    private List<string> fileList = new List<string>();
    private Dictionary<string, float> fileProgress = new Dictionary<string, float>();

    private bool isBuild = false;
    private bool isUploading = false;
    private bool uploadComplete = false;
    private int filesUploaded = 0;
    private Vector2 scrollPosition;

    private string aliyunConfigPath = "Assets/AliyunConfigPath.asset";
    private AliyunConfig aliyunConfig;

    [MenuItem("Window/阿里云OSS资源上传")]
    public static void ShowWindow()
    {
        GetWindow<OSSUploaderEditorWindow>("OSS Uploader").Show();
    }

    private void OnEnable()
    {
        InifData();
        if (!string.IsNullOrEmpty(aliyunConfig.selectedFolderPath))
        {
            PopulateFileList(aliyunConfig.selectedFolderPath);
        }
    }

    private void OnFocus()
    {
        if (!string.IsNullOrEmpty(aliyunConfig.selectedFolderPath))
        {
            PopulateFileList(aliyunConfig.selectedFolderPath);
        }
    }

    private void InifData()
    {
        aliyunConfig = AssetDatabase.LoadAssetAtPath<AliyunConfig>(aliyunConfigPath);
        if (aliyunConfig == null)
        {
            aliyunConfig = ScriptableObject.CreateInstance<AliyunConfig>();
            if (!Directory.Exists(Application.streamingAssetsPath))
            {
                Directory.CreateDirectory(Application.streamingAssetsPath);
            }
            AssetDatabase.CreateAsset(aliyunConfig, aliyunConfigPath);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
        }
    }

    private void OnGUI()
    {
        GUILayout.Label("OSS Uploader", EditorStyles.boldLabel);
        aliyunConfig.accessKeyId = EditorGUILayout.TextField("accessKeyId:", aliyunConfig.accessKeyId);
        aliyunConfig.accessKeySecret = EditorGUILayout.TextField("accessKeySecret:", aliyunConfig.accessKeySecret);
        aliyunConfig.endpoint = EditorGUILayout.TextField("endpoint:", aliyunConfig.endpoint);
        aliyunConfig.bucketName = EditorGUILayout.TextField("bucketName:", aliyunConfig.bucketName);
        aliyunConfig.objectKey = EditorGUILayout.TextField("objectKey:", aliyunConfig.objectKey);

        aliyunConfig.selectedFolderPath = EditorGUILayout.TextField("Folder Path:", aliyunConfig.selectedFolderPath);

        if (GUILayout.Button("选择文件夹"))
        {
            aliyunConfig.selectedFolderPath = EditorUtility.OpenFolderPanel("Select Folder to Upload", "", "");
            if (!string.IsNullOrEmpty(aliyunConfig.selectedFolderPath))
            {
                PopulateFileList(aliyunConfig.selectedFolderPath);
            }
        }

        if (GUILayout.Button("上传文件到阿里云Oss,OSS路径:" + aliyunConfig.objectKey) && !isUploading)
        {
            isUploading = true;
            uploadComplete = false;
            UploadFolder();
        }

        var proStr = "进度:" + filesUploaded + "/" + fileList.Count;
        if (uploadComplete)
        {
            proStr += "    ======    上传完成!!!!";
        }
        else if (isUploading)
        {
            proStr += "    ======    上传中  =====";
        }

        EditorGUILayout.Space(10);
        GUILayout.Label(proStr, new GUIStyle { fontSize = 20, fontStyle = FontStyle.Bold, normal = new GUIStyleState() { textColor = Color.white } });
        EditorGUILayout.Space(10);

        GUILayout.Label("本地当前上传文件路径:" + aliyunConfig.selectedFolderPath);
        EditorGUILayout.Space(10);

        scrollPosition = EditorGUILayout.BeginScrollView(scrollPosition);
        foreach (string file in fileList)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label(file);
            if (fileProgress.ContainsKey(file))
            {
                GUILayout.Label((fileProgress[file] * 100).ToString("F2") + "%");
            }
            GUILayout.EndHorizontal();
        }
        EditorGUILayout.EndScrollView();

        if (uploadComplete)
        {
            GUILayout.Label("OK!", EditorStyles.boldLabel);
        }
    }

    private void PopulateFileList(string folderPath)
    {
        fileList.Clear();
        fileProgress.Clear();
        if (Directory.Exists(folderPath))
        {
            string[] files = Directory.GetFiles(folderPath, "*", SearchOption.AllDirectories);
            fileList.AddRange(files);
        }
        else
        {
            Debug.LogError("当前文件夹不存在:" + folderPath);
        }
    }

    private async void UploadFolder()
    {
        await Task.Yield();
        if (string.IsNullOrEmpty(aliyunConfig.selectedFolderPath))
        {
            Debug.LogError("Folder path is empty.");
            return;
        }

        if (fileList.Count == 0)
        {
            Debug.LogError("No files found in the selected folder.");
            return;
        }

        OssClient client = new OssClient(aliyunConfig.endpoint, aliyunConfig.accessKeyId, aliyunConfig.accessKeySecret);
        try
        {
            filesUploaded = 0;
            foreach (string filePath in fileList)
            {
                await Task.Yield();
                _ = Task.Run(async () =>
                {
                    await Task.Delay(100);
                    string relativePath = filePath.Replace(aliyunConfig.selectedFolderPath, "").TrimStart('\\', '/').Replace('\\', '/').Replace("//", "/");
                    using (var fs = File.Open(filePath, FileMode.Open))
                    {
                        var ossobjectKey = Path.Combine(aliyunConfig.objectKey, relativePath);
                        // Debug.Log(ossobjectKey);
                        PutObjectRequest putObjectRequest = new PutObjectRequest(aliyunConfig.bucketName, ossobjectKey, fs);
                        putObjectRequest.StreamTransferProgress += (object sender, StreamTransferProgressArgs args) =>
                        {
                            var process = (args.TransferredBytes * 100 / args.TotalBytes) / 100.0f;

                            lock (fileProgress)
                            {
                                fileProgress[filePath] = process;
                            }

                            if (process == 1)
                            {
                                filesUploaded++;
                                if (filesUploaded == fileList.Count)
                                {
                                    uploadComplete = true;
                                    isUploading = false;
                                    Debug.Log("上传完成!!!");
                                }
                            }
                        };

                        client.PutObject(putObjectRequest);
                    }
                });
            }
        }
        catch (Exception e)
        {
            Debug.LogError("Error uploading folder: " + e.Message);
        }
    }

    public void OnInspectorUpdate()
    {
        if (isUploading)
        {
            Repaint();
        }
    }
}