#if UNITY_EDITOR
using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditor.PackageManager;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace NPMClient.Editor
{
    public class NPMClientWindow : EditorWindow
    {
        private string username;
        private string password;
        private string email;

        private bool showPassword;
        private bool isProcessing;

        private const float labelWidth = 70f;
        private const float showPasswordButtonWidth = 50f;
        private const float loginButtonWidth = 60f;
        private const float deleteButtonWidth = 160f;
        
        private const string registryURL   = "https://npm.turkugamelab.fi/";
        private const string registryName  = "TUAS-FIT";
        private const string registryScope = "com.tuas-fit";
        private const string shortenRegistryURL  = "npm.turkugamelab.fi";
        private const string loginExecutableName = "npm-login";
        private const string jsonFileName = "npm-login.json";

        [MenuItem("Window/Login as NPM Client")]
        private static void Open()
        {
            GetWindow(typeof(NPMClientWindow), false, "Login");
        }

        private void OnGUI()
        {
            DrawUsernameField();
            DrawPasswordField();
            DrawEmailField();
            DrawLoginButton();
            DrawDeleteCredentialsCachedButton();
        }

        private void DrawUsernameField()
        {
            GUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("Username:", GUILayout.Width(labelWidth));
            username = EditorGUILayout.TextField(username);
            GUILayout.EndHorizontal();
        }

        private void DrawPasswordField()
        {
            GUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("Password:", GUILayout.Width(labelWidth));
            password = showPassword? EditorGUILayout.TextField(password) : EditorGUILayout.PasswordField(password);

            if (GUILayout.Button("Show", GUILayout.Width(showPasswordButtonWidth)))
                showPassword = !showPassword;
            
            GUILayout.EndHorizontal();
        }
        
        private void DrawEmailField()
        {
            GUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("Email:", GUILayout.Width(labelWidth));
            email = EditorGUILayout.TextField(email);
            GUILayout.EndHorizontal();
        }
        
        private void DrawLoginButton()
        {
            if (isProcessing)
            {
                GUILayout.BeginHorizontal();
                GUILayout.FlexibleSpace();
                GUILayout.Label("Processing...");
                GUILayout.FlexibleSpace();
                GUILayout.EndHorizontal();
            }
            else
            {
                GUILayout.BeginHorizontal();
                GUILayout.FlexibleSpace();
                if (GUILayout.Button("Login", GUILayout.Width(loginButtonWidth)))
                    Login();
                GUILayout.FlexibleSpace();
                GUILayout.EndHorizontal();
            }
        }
        
        private void DrawDeleteCredentialsCachedButton()
        {
            GUILayout.BeginHorizontal();
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("Delete cached credential", GUILayout.Width(deleteButtonWidth)))
                DeleteCredentialFiles();
            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();
        }

        private void Login()
        {
            CreateLoginData();
            RunExternalLoginApp();
        }

        private void DeleteCredentialFiles()
        {
            var userDirectory = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            var npmrcFile = Path.Combine(userDirectory, ".npmrc");
            var upmConfigFile = Path.Combine(userDirectory, ".upmconfig.toml");

            if (File.Exists(npmrcFile))
            {
                File.Delete(npmrcFile);
                Debug.Log("Successfully deleted .npmrc file");
            }

            if (File.Exists(upmConfigFile))
            {
                File.Delete(upmConfigFile);
                Debug.Log("Successfully deleted .upmconfig.toml file");
            }
        }

        private void CreateLoginData()
        {
            if (string.IsNullOrEmpty(username) || string.IsNullOrEmpty(password) || string.IsNullOrEmpty(email))
            {
                Debug.LogError("Username, password or email is empty");
                return;
            }
            
            var loginData = new LoginData
            {
                Username = username,
                Password = password,
                Email = email,
                RegistryURL = registryURL
            };

            var userDirectory = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            var outputJsonPath = Path.Combine(userDirectory, jsonFileName);

            File.WriteAllText(outputJsonPath, JsonUtility.ToJson(loginData, true));
        }

        private void RunExternalLoginApp()
        {
            var files = Directory.GetFiles(Directory.GetCurrentDirectory(), "*.exe", SearchOption.AllDirectories);
            var loginExecutable = files.FirstOrDefault(f => f.Contains(loginExecutableName));

            if (loginExecutable == null)
            {
                Debug.LogError($"{loginExecutableName} executable not found");
                return;
            }

            var process = new Process();

            try
            {
                process.EnableRaisingEvents = false;
                process.StartInfo.FileName = loginExecutable;
                process.Start();
                process.WaitForExit();
                
                var token = ExtractTokenFromNPMRC();

                if (!string.IsNullOrEmpty(token))
                {
                    CreateUPMConfig(token);
                    AddRegistryToProjectManifest();
                }
            }
            catch (Exception e)
            {
                Debug.LogError(e);
            }
            finally
            {
                process.Dispose();
            }
        }
        
        private string ExtractTokenFromNPMRC()
        {
            var userDirectory = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            var jsonFilePath = Path.Combine(userDirectory, jsonFileName);
            var npmrcFile = Path.Combine(userDirectory, ".npmrc");
            
            if (File.Exists(jsonFilePath))
                File.Delete(jsonFilePath);
            
            if (File.Exists(npmrcFile))
            {
                var lines = File.ReadAllLines(npmrcFile);
                var registryLine = lines.FirstOrDefault(l => l.Contains(shortenRegistryURL));

                if (!string.IsNullOrEmpty(registryLine))
                    return registryLine.Split('=', 2)[1].Replace("\"", "");
            }

            return string.Empty;
        }
        
        private void CreateUPMConfig(string token)
        {
            var userDirectory = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            var upmConfigFile = Path.Combine(userDirectory, ".upmconfig.toml");
            
            File.WriteAllText(upmConfigFile, $"[npmAuth.\"{registryURL}\"]\n" +
                                             $"token = \"{token}\"\n" +
                                             $"email = \"{email}\"\n" +
                                             $"alwaysAuth = true");
        }
        
        private void AddRegistryToProjectManifest()
        {
            var manifestFile = Path.Combine(Directory.GetCurrentDirectory(), @"Packages\manifest.json");
            
            if (!File.Exists(manifestFile))
            {
                Debug.LogError("Manifest file not found");
                return;
            }
            
            typeof(Client).GetMethods(BindingFlags.Static | BindingFlags.NonPublic)
                          .First(x => x.Name =="AddScopedRegistry" && x.GetParameters().Length == 3)
                          .Invoke(obj: null, parameters: new object[] { registryName, registryURL, new[] { registryScope }});
        }

        [Serializable]
        public struct LoginData
        {   
            public string Username;
            public string Password;
            public string Email;
            public string RegistryURL;
        }
    }
}
#endif