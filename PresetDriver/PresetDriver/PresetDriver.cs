using System;
using System.Collections.Generic;
using System.Linq;
using Crestron.SimplSharp;
using Crestron.SimplSharp.CrestronIO;
using Newtonsoft.Json;

namespace PresetDriver
{
    public delegate void InitializedEvent(ushort state);
    public delegate void WindowPresetChangedDelegate(ushort value);

    public delegate void UshortValue(ushort value);
    public delegate void OutputValueChangedDelegate(ushort index, ushort value);

    //public delegate void InitializeHandler(ushort state);

    public delegate void PresetCountHandler(ushort count);
    public delegate void PresetNameHandler(ushort index, SimplSharpString name);
    public delegate void WindowPresetHandler(ushort value);
    public delegate void OutputValueHandler(ushort index, ushort value); 
    public class PresetClient
    {
        
        private bool _initialized;
        private string _debugName;
        public ushort DebugEnable = 0;
        private readonly PresetFileManager _fm;
        public string _fileName = "advance.json";
        private Dictionary<string, PresetRecord> _presets = new Dictionary<string, PresetRecord>();
        private List<string> _presetNames = new List<string>();
        private string _currentPreset;
        public InitializedEvent Initialized { get; set; }
        public  WindowPresetChangedDelegate WindowPresetChanged { get; set; }
        public  WindowPresetChangedDelegate WindowPreset2Changed { get; set; }
        
        public OutputValueChangedDelegate  OutputValueChanged { get; set; }
        public PresetCountHandler OnPresetCount { get; set; }
        public PresetNameHandler OnPresetName { get; set; }  
        public UshortValue Output1Changed { get; set; }
        public UshortValue Output2Changed { get; set; }
        public UshortValue Output3Changed { get; set; }
        public UshortValue Output4Changed { get; set; }
        public UshortValue Output5Changed { get; set; }
        public UshortValue Output6Changed { get; set; }
        public UshortValue Output7Changed { get; set; }
        public UshortValue Output8Changed { get; set; }
        public UshortValue Output9Changed  { get; set; }
        public UshortValue Output10Changed { get; set; }
        public UshortValue Output11Changed { get; set; }
        public UshortValue Output12Changed { get; set; }
        public UshortValue Output13Changed { get; set; }
        public UshortValue Output14Changed { get; set; }
        public UshortValue Output15Changed { get; set; }
        public UshortValue Output16Changed { get; set; }
        public UshortValue Output17Changed { get; set; }
        public UshortValue Output18Changed { get; set; }
        public UshortValue Output19Changed { get; set; }
        public UshortValue Output20Changed { get; set; }

        private UshortValue[] OutputTaps() => new UshortValue[]
        {
            Output1Changed, Output2Changed,  Output3Changed, Output4Changed, Output5Changed,
            Output6Changed, Output7Changed, Output8Changed, Output9Changed, Output10Changed,
            Output11Changed, Output12Changed, Output13Changed, Output14Changed, Output15Changed,
            Output16Changed,  Output17Changed,  Output18Changed,  Output19Changed,  Output20Changed
        };

        private const string DefaultJson =
            @"{
  ""presets"": {
    ""Preset1"": { ""windowPreset1"": 1, ""windowPreset2"": 0, ""outputs"": [0,0,0,1,2,4,1,0, 0,0,0,0,0,0,0,0, 0,0,0,0] },
    ""Preset2"": { ""windowPreset1"": 1, ""windowPreset2"": 0, ""outputs"": [0,0,0,1,2,4,1,0, 0,0,0,0,0,0,0,0, 0,0,0,0] },
    ""Preset3"": { ""windowPreset1"": 1, ""windowPreset2"": 0, ""outputs"": [0,0,0,1,2,4,1,0, 0,0,0,0,0,0,0,0, 0,0,0,0] },
    ""Preset4"": { ""windowPreset1"": 1, ""windowPreset2"": 0, ""outputs"": [0,0,0,1,2,4,1,0, 0,0,0,0,0,0,0,0, 0,0,0,0] },
    ""Preset5"": { ""windowPreset1"": 1, ""windowPreset2"": 0, ""outputs"": [0,0,0,1,2,4,1,0, 0,0,0,0,0,0,0,0, 0,0,0,0] }
  }
}";
        public PresetClient()
        {
            //CrestronConsole.PrintLine("[PresetManager] ctor filePath='{0}'", _fm.FileName);
            _fm = new PresetFileManager(msg => Debug(msg));
       
        }

        public void Debug(string message)
        {
            if (DebugEnable >= 1)
            {
                CrestronConsole.PrintLine(" [" + _debugName + "] " + message);
            }
        }

        public bool FileExists()
        {
            return _fm.Exists();
        }

        public void Initialize(string debugName)
        {
            _debugName = debugName;
            Debug("Initialize called");

            // Build absolute path here (safer than ctor-time)
            _fm.Initialize(_fileName);
            Debug("resolved filePath='" + _fm.FileName + "'"); LocateFile();
            LoadPresets();
            _initialized = true;
            Initialized?.Invoke(1);
        }

        private void LocateFile()
        {
            try
            {
                Debug("Locating file: " +  _fm.FileName);
                if (!_fm.Exists())
                {
                    Debug("creating file: " + _fm.FileName);
                    _fm.WriteAll(DefaultJson);
                    Debug("file created: " + _fm.FileName);
                    
                }
                else
                {
                    Debug("file found " + _fm.FileName);
                }

            }
            catch(System.Exception ex)
            {
                Debug("locate file error: " + ex.Message);
                
            }
            
        }

        private void LoadPresets()
        {
            try
            {
                var json = _fm.ReadAll();
                if (string.IsNullOrWhiteSpace(json))
                {
                    Debug("LoadPresets: file empty, writing defaults.");
                    _fm.WriteAll(DefaultJson);
                    json = DefaultJson;
                }

                PresetConfig cfg = null;
                try
                {
                    cfg = JsonConvert.DeserializeObject<PresetConfig>(json);
                }
                catch (Exception ex)
                {
                    Debug("LoadPresets: parse error " + ex.Message + " writing defaults.");
                    _fm.WriteAll(DefaultJson);
                    cfg = JsonConvert.DeserializeObject<PresetConfig>(DefaultJson);
                }

                if (cfg?.presets == null || cfg.presets.Count == 0)
                {
                    Debug("LoadPresets: invalid or empty JSON, writing defaults.");
                    _fm.WriteAll(DefaultJson);
                    cfg = JsonConvert.DeserializeObject<PresetConfig>(DefaultJson);
                }

                _presets = cfg.presets.ToDictionary(kv => kv.Key, kv => NormalizeRecord(kv.Value));

                _presetNames = _presets.Keys.OrderBy(n => n).ToList();
                _currentPreset = _presetNames.FirstOrDefault();

                Debug($"LoadPresets: {_presets.Count} presets loaded.");
            }
            catch (Exception ex)
            {
                Debug("LoadPresets error: " + ex.Message);
            }
        }

        private static PresetRecord NormalizeRecord(PresetRecord pr)
        {
            if(pr == null) pr = new PresetRecord();
            EnsureOutputSize20(pr);
            return pr;
        }
        private void PublishPresetList()
        {
            try
            {
                var count = (ushort)(_presetNames.Count);
                OnPresetCount?.Invoke(count);

                // 1-based indices are friendlier for S+
                ushort idx = 1;
                foreach (var name in _presetNames)
                {
                    OnPresetName?.Invoke(idx, new SimplSharpString(name));
                    idx++;
                }
            }
            catch (System.Exception ex)
            {
                Debug("PublishPresetList error: " + ex.Message);
            }
        }

        public void RecallPresetByIndex(ushort index)
        {
            Debug($"RecallPresetByIndex({index}) "
                  + $"names.Count={(_presetNames == null ? -1 : _presetNames.Count)} "
                  + $"current='{_currentPreset ?? "(none)"}'");
            // guard: no list yet
            if (_presetNames == null || _presetNames.Count == 0)
            {
                Debug("RecallPresetByIndex: _presetNames is null/empty; did LoadPresets() run?");
                return;
            }

            // guard: out of range
            if (index == 0 || index > _presetNames.Count)
            {
                Debug($"RecallPresetByIndex: index out of range (1..{_presetNames.Count}), got {index}");
                // helpful peek at the first few names
                var peek = string.Join(",", _presetNames.Take(5).ToArray());
                Debug($"RecallPresetByIndex: names[1..]={peek}{(_presetNames.Count > 5 ? ",..." : "")}");
                return;
            }

            // map index -> name
            var name = _presetNames[index - 1];
            Debug($"RecallPresetByIndex: index {index} maps to name='{name}'");

            // sanity: name exists in dictionary?
            if (!_presets.ContainsKey(name))
            {
                Debug($"RecallPresetByIndex: name '{name}' not found in _presets (keys={_presets.Count})");
                return;
            }

            // final hand-off
            Debug($"RecallPresetByIndex: calling RecallPresetByName('{name}')");
            RecallPresetByName(new SimplSharpString(name));
        }
        public void RecallPresetByName(SimplSharpString name)
        {
            Debug("RecallPresetByName(entry) name='" + (name == null ? "(null)" : name.ToString()) + "'");

            var key = name?.ToString();
            if (string.IsNullOrEmpty(key) || !_presets.ContainsKey(key)) return;

            _currentPreset = key;
            var pr = _presets[key];
            EnsureOutputSize20(pr);

            var w1 = (ushort)pr.windowPreset1;
            var w2 = (ushort)pr.windowPreset2;
            WindowPresetChanged?.Invoke(w1);
            WindowPreset2Changed?.Invoke(w2);
            Debug($"Recalled '{key}' window1={w1} window2={w2}");
            var taps = OutputTaps();
            for(var i = 0; i < taps.Length; i++)
                taps[i]?.Invoke((ushort)pr.outputs[i]);

        }
        public void RequestWindowPreset1()
        {
            if (string.IsNullOrEmpty(_currentPreset)) return;
            var pr = _presets[_currentPreset];
            WindowPresetChanged?.Invoke((ushort)pr.windowPreset1);
        }
        public void RequestWindowPreset2()
        {
            if (string.IsNullOrEmpty(_currentPreset)) return;
            var pr = _presets[_currentPreset];
            WindowPreset2Changed?.Invoke((ushort)pr.windowPreset2);
        }

        public void RequestOutput(ushort index)
        {
            if (string.IsNullOrEmpty(_currentPreset)) return;
            var pr = _presets[_currentPreset];
            if (index == 0 || index > 20) return;
            EnsureOutputSize20(pr);
            ushort val = 0;
            val = pr.outputs[index - 1];
            var taps = OutputTaps();
            taps[index - 1]?.Invoke(val);


        }

        public void RequestAllOutputs()
        {
            if(string.IsNullOrEmpty(_currentPreset)) return;
            var pr = _presets[_currentPreset];
            EnsureOutputSize20(pr);
            var taps = OutputTaps();
            for(int i = 0; i < taps.Length; i++)
                taps[i]?.Invoke((ushort)pr.outputs[i]);
        }

        private static void EnsureOutputSize20(PresetRecord pr)
        {
            if (pr.outputs == null)
                pr.outputs = new List<ushort>(new ushort[20]);
            else
            {
                while(pr.outputs.Count < 20) pr.outputs.Add(0);
            }
        }

        public int SavePresets()
        {
            try
            {
                Debug("Saving presets: entry");
                if (_fm == null || string.IsNullOrWhiteSpace(_fm.FileName))
                {
                    Debug("SavePresets: file manager/file name not ready.");
                    return 0;

                }

                if (_presets == null)
                {
                    Debug("SavePresets: _presets is null");
                    return 0;
                }
                var presetCount = _presets.Count;
                var current = _currentPreset ?? "(none)";
                Debug($"SavePresets: presets={presetCount} current='{current}' target='{_fm.FileName}'");
                var cfg = new PresetConfig { presets = _presets };
                var json = JsonConvert.SerializeObject(cfg, Formatting.Indented);
                Debug($"SavePresets: json length={json.Length}");
                _fm.WriteAll(json);
                Debug("SavePresets: write OK");
                var readBack = _fm.ReadAll();
                if (string.IsNullOrWhiteSpace(readBack))
                {
                    Debug("SavePresets: verify FAILED (empty readback).");
                    return 0;
                }

                var verify = JsonConvert.DeserializeObject<PresetConfig>(readBack);
                var vCount = (verify?.presets?.Count) ?? 0;
                Debug($"SavePresets: verify OK (presets={vCount})");

                return 1;
            }
            catch (System.Exception ex)
            {
                Debug("SavePresets: EXCEPTION " + ex.Message);
                return 0;
            }
        }
        
        public int OverwriteCurrentPreset8(ushort window,
            ushort o1, ushort o2, ushort o3, ushort o4,
            ushort o5, ushort o6, ushort o7, ushort o8)
        {
            if (string.IsNullOrEmpty(_currentPreset))
            {
                Debug("OverwriteCurrentPreset8: no current preset selected.");
                return 0;
            }

            if (!_presets.ContainsKey(_currentPreset))
                _presets[_currentPreset] = new PresetRecord();

            var pr = _presets[_currentPreset];
            pr.windowPreset1 = window;
            pr.outputs = new List<ushort> { o1, o2, o3, o4, o5, o6, o7, o8 };

            Debug($"OverwriteCurrentPreset8: '{_currentPreset}' win={window} outs=[{o1},{o2},{o3},{o4},{o5},{o6},{o7},{o8}]");

            var rc = SavePresets();

            // Optional: re-emit so SIMPL+ reflects the saved values immediately
            if (rc != 0)
            {
                WindowPresetChanged?.Invoke(window);

                var taps = new[] { Output1Changed, Output2Changed, Output3Changed, Output4Changed,
                    Output5Changed, Output6Changed, Output7Changed, Output8Changed };

                taps[0]?.Invoke(o1); taps[1]?.Invoke(o2); taps[2]?.Invoke(o3); taps[3]?.Invoke(o4);
                taps[4]?.Invoke(o5); taps[5]?.Invoke(o6); taps[6]?.Invoke(o7); taps[7]?.Invoke(o8);
            }

            return rc;
        }
        public int OverwriteCurrentPreset20Masked(
            ushort window1, ushort window2, uint applyMask,
            ushort o1, ushort o2, ushort o3, ushort o4,
            ushort o5, ushort o6, ushort o7, ushort o8, ushort o9, ushort o10,
            ushort o11, ushort o12, ushort o13, ushort o14, ushort o15,
            ushort o16, ushort o17, ushort o18, ushort o19, ushort o20)
        {
            try
            {
                if (string.IsNullOrEmpty(_currentPreset))
                {
                    Debug("OverwriteCurrentPreset8Masked: no current preset selected.");
                    return 0;
                }

                if (!_presets.ContainsKey(_currentPreset))
                    _presets[_currentPreset] = new PresetRecord();

                var pr = _presets[_currentPreset];
                EnsureOutputSize20(pr);

                // Apply windows per mask
                if ((applyMask & 0x000000001u) != 0)
                    pr.windowPreset1 = window1;
                if ((applyMask & 0x000000002u) != 0)
                    pr.windowPreset2 = window2;

                var incoming = new[] { o1, o2, o3, o4, o5, o6, o7, o8, o9, o10, o11, o12, o13, o14, o15, o16, o17, o18, o19, o20 };
                for (int i = 0; i < 20; i++)
                {
                    uint bit = 1u << (i + 2); // output i -> bit i+1
                    if ((applyMask & bit) != 0)
                        pr.outputs[i] = incoming[i];
                }

                Debug($"OverwriteCurrentPreset20Masked: mask=0x{applyMask:X} window={window1} " +
                      $"outs=[{string.Join(",", pr.outputs)}]");

                var rc = SavePresets();
                if (rc == 0) return 0;
                if((applyMask & 0x00000001u) != 0)WindowPresetChanged?.Invoke(pr.windowPreset1);
                if((applyMask & 0x00000002u) != 0)WindowPreset2Changed?.Invoke(pr.windowPreset2);
                var taps = OutputTaps();
                for (int i = 0; i < taps.Length; i++)
                {
                    uint bit = 1u << (i + 2);
                    if((applyMask & bit) != 0)
                        taps[i]?.Invoke(pr.outputs[i]);
                }

                return 1;
            }
            catch (System.Exception ex)
            {
                Debug("OverwriteCurrentPreset8Masked: EXCEPTION " + ex.Message);
                return 0;
            }
        }
        public int OverwriteCurrentPreset(ushort window, IList<ushort> outputs)
        {
            if (string.IsNullOrEmpty(_currentPreset))
            {
                Debug("OverwriteCurrentPreset: no current preset selected.");
                return 0;
            }
            if (!_presets.ContainsKey(_currentPreset))
                _presets[_currentPreset] = new PresetRecord();

            var pr = _presets[_currentPreset];
            pr.windowPreset1 = window;
            pr.outputs = outputs == null ? new List<ushort>() : new List<ushort>(outputs);

            Debug($"OverwriteCurrentPreset: '{_currentPreset}' win={window} outs=[{string.Join(",", pr.outputs)}]");
            return SavePresets();
        }
        

        public void SetFileName(SimplSharpString name)
        {
            var s = (name ?? string.Empty).ToString().Trim();
            _fileName = NormalizeJsonFileName(s);
            Debug("SetFileName -> '" + _fileName + "'");
        }

        private static string NormalizeJsonFileName(string input)
        {
            if (string.IsNullOrEmpty(input))
            {
                return "advance.json";
            }

            var s = input.Replace('\\', '/').Trim().TrimStart('/');
            var cleaned = new string(s
                .Where(ch => char.IsLetterOrDigit(ch) || ch == '-' || ch == '_' || ch == '.' || ch == '/').ToArray());
            if (!cleaned.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
            {
                cleaned += ".json";
            }
            return cleaned;
        }

    }

    public sealed class PresetFileManager
    {
        private readonly Action<string> _log;
        public string FileName { get; private set; }

        public PresetFileManager(Action<string> log = null)
        {
            _log = log;
        }

        private void Log(string m)
        {
            _log?.Invoke(m);
        }

        private void Log(string fmt, params object[] args) => _log?.Invoke(string.Format(fmt, args));
        

        public void Initialize(string file)
        {
            if (string.IsNullOrWhiteSpace(file))
                throw new System.ArgumentException("file cannot be empty", "file");

            var root = Directory.GetApplicationRootDirectory();
            if (string.IsNullOrWhiteSpace(root))
                root = "/NVRAM/";                    // fallback when called too early

            root = root.Replace('\\','/');           // normalize
            if (!root.EndsWith("/")) root += "/";

            FileName = root + file.Replace('\\','/').TrimStart('/');
            Log("[PresetFileManager] root='{0}' file='{1}' -> '{2}'",
                root, file, FileName);
        }
        public bool Exists() => !string.IsNullOrWhiteSpace(FileName) && File.Exists(FileName);

        public void EnsureDirectory()
        {
            var dir = Path.GetDirectoryName(FileName);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                Directory.CreateDirectory(dir);
        }
        public void WriteAll(string content)
        {
            EnsureDirectory();
            var len = content?.Length ?? 0;
            Log("[PresetFileManager] WriteAll path='{0}' len={1}", FileName, len);
            using (var fs = new FileStream(FileName, FileMode.Create, FileAccess.Write))
            using (var sw = new StreamWriter(fs))
                sw.Write(content);
            try
            {
                var fi = new FileInfo(FileName);
                fi.Refresh();
                Log("[PresetFileManager] Verify exists={0} size={1}", fi.Exists, fi.Exists ? fi.Length : 0);
            }
            catch { /* best-effort */ }
        } 
        public string ReadAll()
        {
            Log("[PresetFileManager] ReadAll path='{0}'", FileName);

            using (var fs = new FileStream(FileName, FileMode.Open, FileAccess.Read))
            using (var sr = new StreamReader(fs))
                return sr.ReadToEnd();
        } 
    }
    public class PresetConfig
    {
        public Dictionary<string, PresetRecord> presets { get; set; }
    }
    public class PresetRecord
    {
        public ushort windowPreset1 { get; set; }
        public ushort windowPreset2 { get; set; }
        public List<ushort> outputs { get; set; }
    }
}