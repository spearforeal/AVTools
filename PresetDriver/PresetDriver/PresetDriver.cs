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

        private const string DefaultJson =
            @"{
  ""presets"": {
    ""Preset1"": { ""windowPreset"": 1, ""outputs"": [0,0,0,1,2,4,1,0] },
    ""Preset2"": { ""windowPreset"": 1, ""outputs"": [0,0,0,1,2,4,1,0] },
    ""Preset3"": { ""windowPreset"": 1, ""outputs"": [0,0,0,1,2,4,1,0] },
    ""Preset4"": { ""windowPreset"": 1, ""outputs"": [0,0,0,1,2,4,1,0] },
    ""Preset5"": { ""windowPreset"": 1, ""outputs"": [0,0,0,1,2,4,1,0] }
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
            Debug("resolved filePath='" + _fm.FileName + "'");
            LocateFile();
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

                var cfg = JsonConvert.DeserializeObject<PresetConfig>(json);
                if (cfg?.presets == null || cfg.presets.Count == 0)
                {
                    Debug("LoadPresets: invalid or empty JSON, writing defaults.");
                    _fm.WriteAll(DefaultJson);
                    cfg = JsonConvert.DeserializeObject<PresetConfig>(DefaultJson);
                }

                _presets = cfg.presets
                    .ToDictionary(kv => kv.Key, kv => kv.Value ?? new PresetRecord());

                _presetNames = _presets.Keys.OrderBy(n => n).ToList();
                _currentPreset = _presetNames.FirstOrDefault();

                Debug($"LoadPresets: {_presets.Count} presets loaded.");
            }
            catch (Exception ex)
            {
                Debug("LoadPresets error: " + ex.Message);
            }
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

            var taps = new[]
            {
                Output1Changed, Output2Changed, Output3Changed, Output4Changed,
                Output5Changed, Output6Changed, Output7Changed, Output8Changed
            };
            var key = name?.ToString();
            if (string.IsNullOrEmpty(key) || !_presets.ContainsKey(key)) return;

            _currentPreset = key;
            var pr = _presets[key];

            // publish window preset
            var wp = (ushort)pr.windowPreset;
            WindowPresetChanged?.Invoke(wp);
            Debug($"Recalled '{key}' windowPreset={wp}");

            // publish outputs as index/value (1-based)
            for (var i = 0; i < taps.Length; i ++)
            {
                ushort val = 0;
                if (pr.outputs != null && i < pr.outputs.Count)
                {
                    val = (ushort)pr.outputs[i];
                }

                taps[i]?.Invoke(val);
            }
        }
        public void RequestWindowPreset()
        {
            if (string.IsNullOrEmpty(_currentPreset)) return;
            var pr = _presets[_currentPreset];
            WindowPresetChanged?.Invoke((ushort)pr.windowPreset);
        }

        public void RequestOutput(ushort index)
        {
            if (string.IsNullOrEmpty(_currentPreset)) return;
            var pr = _presets[_currentPreset];
            if (index == 0 || index > 8) return;
            ushort val = 0;

            if (pr.outputs != null && index <= pr.outputs.Count)
            {
                val = (ushort)pr.outputs[index - 1];
            }

            switch (index)
            {
                case 1: Output1Changed?.Invoke(val); break;
                case 2: Output2Changed?.Invoke(val); break;
                case 3: Output3Changed?.Invoke(val); break;
                case 4: Output4Changed?.Invoke(val); break;
                case 5: Output5Changed?.Invoke(val); break;
                case 6: Output6Changed?.Invoke(val); break;
                case 7: Output7Changed?.Invoke(val); break;
                case 8: Output8Changed?.Invoke(val); break;
            }

        }

        public void RequestAllOutputs()
        {
            if(string.IsNullOrEmpty(_currentPreset)) return;
            var pr = _presets[_currentPreset];
            var taps = new UshortValue[]
            {
                Output1Changed, Output2Changed, Output3Changed, Output4Changed,
                Output5Changed, Output6Changed, Output7Changed, Output8Changed
            };
            for (int i = 0; i < taps.Length; i++)
            {
                ushort val = 0;
                if (pr.outputs != null && i < pr.outputs.Count)
                {
                    val = (ushort)pr.outputs[i];
                    
                }
                taps[i]?.Invoke(val);
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
            pr.windowPreset = window;
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
        public int OverwriteCurrentPreset8Masked(
            ushort window, ushort applyMask,
            ushort o1, ushort o2, ushort o3, ushort o4,
            ushort o5, ushort o6, ushort o7, ushort o8)
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
                EnsureOutputsSize(pr);

                // Bit 0 -> window, Bits 1..8 -> outputs 1..8
                if ((applyMask & 0x0001) != 0)
                    pr.windowPreset = window;

                var incoming = new[] { o1, o2, o3, o4, o5, o6, o7, o8 };
                for (int i = 0; i < 8; i++)
                {
                    ushort bit = (ushort)(1 << (i + 1)); // output i -> bit i+1
                    if ((applyMask & bit) != 0)
                        pr.outputs[i] = incoming[i];
                }

                Debug($"OverwriteCurrentPreset8Masked: mask=0x{applyMask:X} window={window} " +
                      $"outs=[{string.Join(",", pr.outputs)}]");

                return SavePresets();
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
            pr.windowPreset = window;
            pr.outputs = outputs == null ? new List<ushort>() : new List<ushort>(outputs);

            Debug($"OverwriteCurrentPreset: '{_currentPreset}' win={window} outs=[{string.Join(",", pr.outputs)}]");
            return SavePresets();
        }
        
        private static void EnsureOutputsSize(PresetRecord pr)
        {
            if (pr.outputs == null)
                pr.outputs = new List<ushort>(new ushort[8]);
            else
                while (pr.outputs.Count < 8) pr.outputs.Add(0);
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
        public ushort windowPreset { get; set; }
        public List<ushort> outputs { get; set; }
    }
}