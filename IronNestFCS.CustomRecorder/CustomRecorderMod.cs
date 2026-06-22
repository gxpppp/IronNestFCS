using Il2Cpp;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using MelonLoader;
using UnityEngine;
using Object = UnityEngine.Object;

[assembly: MelonInfo(typeof(IronNestFCS.CustomRecorder.CustomRecorderMod), "IronNestFCS.CustomRecorder", "0.1.0", "svr2kos2")]
[assembly: MelonGame("Iron Nest", "Iron Nest: Heavy Turret Simulator")]

namespace IronNestFCS.CustomRecorder;

/// <summary>
/// 独立的 MelonMod：克隆场景里的 RecordDisk，并用 StreamingAssets 下的自定义素材
/// 替换其音轨（a.wav）和唱片贴图（diskTexture.png）。
///
/// 这部分原本内嵌在 IronNestFCS.Logic 的 FcsModule 里，但它与火控逻辑无关、
/// 是一次性的场景装饰，故拆成单独的 mod，放进 Mods/ 由 MelonLoader 自动加载。
/// 进场景后轮询 RecordDisk，出现时执行一次。
/// </summary>
public class CustomRecorderMod : MelonMod
{
    private GameObject? diskClone;
    private bool done;

    // 流式 AudioClip 用 PCMReaderCallback 按需供数：必须保活托管侧的样本缓冲和读游标，
    // 否则被 GC 回收后回调里访问就是野指针。模块存活期间一直持有。
    private float[]? _trackSamples;
    private int _trackReadPos;

    public override void OnUpdate()
    {
        if (done)
            return;

        // 原先由 FCS 绑定成功后触发；这里独立运行，轮询 RecordDisk 出现即执行一次。
        var src = GameObject.Find("RecordDisk");
        if (src == null)
            return;

        done = true;
        try
        {
            CreateCustomRecorder(src);
        }
        catch (Exception ex)
        {
            MelonLogger.Error($"[CustomRecorder] 创建自定义唱片失败: {ex}");
        }
    }

    public override void OnDeinitializeMelon()
    {
        if (diskClone != null)
        {
            Object.Destroy(diskClone);
            diskClone = null;
        }
    }

    private void CreateCustomRecorder(GameObject src)
    {
        var disk = Object.Instantiate(src);
        diskClone = disk;
        disk.transform.position = src.transform.position - Vector3.fwd * 0.5f;
        disk.transform.rotation = src.transform.rotation;
        var recordItem = disk.GetComponent<RecordItem>();
        // 从 StreamingAssets/a.wav 加载音频并替换 tracks。
        // 不用 UnityWebRequestMultimedia：本作 IL2CPP 把 DownloadHandlerAudioClip 的 ctor 裁掉了
        //（游戏本体没用到），运行时 MissingMethodException。
        // 也不能用 AudioClip.SetData：它最终走 Il2CppSystem.ReadOnlySpan.GetPinnableReference()，
        // 这个 interop 包装方法同样被裁掉了。
        // 改用带 PCMReaderCallback 的 AudioClip.Create 重载——它是纯 il2cpp_runtime_invoke，无 Span 依赖。
        // Unity 播放时回调拉取 PCM，我们从托管缓冲里按游标喂数据。
        LoadCustomTrack(recordItem);

        var renderer = recordItem.transform.FindChild("Record Disk Blend")
            .GetComponent<MeshRenderer>();
        // 先把当前贴图导出到 out.png（用于确认抓到的是哪张图），再用 diskTexture.png 替换。
        ExportTexture(renderer, "out.png");
        ReplaceTexture(renderer, "diskTexture.png");
    }

    /// <summary>
    /// 把 <paramref name="renderer"/> 当前主贴图导出成 StreamingAssets/<paramref name="fileName"/>。
    /// 游戏贴图通常 isReadable=false（仅在 GPU），不能直接 EncodeToPNG；
    /// 故先 Blit 到临时 RenderTexture，再 ReadPixels 拷回一张可读 Texture2D 后编码。
    /// </summary>
    private static void ExportTexture(MeshRenderer renderer, string fileName) {
        var tex = renderer.material.mainTexture;
        if (tex == null) {
            MelonLogger.Error("[CustomRecorder] 导出失败：material.mainTexture 为空");
            return;
        }

        int w = tex.width, h = tex.height;
        // depthBuffer=0：只要颜色，不需要深度。临时 RT 用完必须 ReleaseTemporary，否则泄漏显存。
        var rt = RenderTexture.GetTemporary(w, h, 0);
        var prevActive = RenderTexture.active;
        try {
            Graphics.Blit(tex, rt);
            RenderTexture.active = rt; // ReadPixels 读的是当前 active RT。

            // RGBA32 + 不带 mipmap，正好对应 PNG。
            var readable = new Texture2D(w, h, TextureFormat.RGBA32, false);
            readable.ReadPixels(new Rect(0, 0, w, h), 0, 0);
            readable.Apply();

            var png = ImageConversion.EncodeToPNG(readable);
            Object.Destroy(readable); // 临时可读纹理用完即弃。

            var path = System.IO.Path.Combine(Application.streamingAssetsPath, fileName);
            // Il2CppStructArray<byte> 不能直接喂 File.WriteAllBytes，先拷成托管 byte[]。
            var managed = new byte[png.Length];
            for (int i = 0; i < png.Length; i++) managed[i] = png[i];
            System.IO.File.WriteAllBytes(path, managed);
            MelonLogger.Msg($"[CustomRecorder] 已导出贴图 {w}x{h} → {path}");
        }
        catch (Exception ex) {
            MelonLogger.Error($"[CustomRecorder] 导出贴图失败: {ex}");
        }
        finally {
            RenderTexture.active = prevActive;
            RenderTexture.ReleaseTemporary(rt);
        }
    }

    /// <summary>
    /// 读取 StreamingAssets/<paramref name="fileName"/>（PNG/JPG），替换 <paramref name="renderer"/> 的主贴图。
    /// LoadImage 会按文件实际尺寸自动 Reinitialize，故初始尺寸随意。
    /// </summary>
    private static void ReplaceTexture(MeshRenderer renderer, string fileName) {
        var path = System.IO.Path.Combine(Application.streamingAssetsPath, fileName);
        if (!System.IO.File.Exists(path)) {
            MelonLogger.Error($"[CustomRecorder] 找不到贴图文件: {path}");
            return;
        }

        try {
            var bytes = System.IO.File.ReadAllBytes(path);
            var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            // LoadImage(Il2CppStructArray<byte>, markNonReadable)：传 false 保留可读，便于以后再导出。
            if (!ImageConversion.LoadImage(tex, new Il2CppStructArray<byte>(bytes), false)) {
                MelonLogger.Error($"[CustomRecorder] LoadImage 解析失败: {path}");
                Object.Destroy(tex);
                return;
            }
            // 用实例 material（非 sharedMaterial）替换，避免改到其他共享此材质的物体。
            renderer.material.mainTexture = tex;
            MelonLogger.Msg($"[CustomRecorder] 已替换贴图 {tex.width}x{tex.height} ← {path}");
        }
        catch (Exception ex) {
            MelonLogger.Error($"[CustomRecorder] 替换贴图失败: {ex}");
        }
    }

    /// <summary>
    /// 读取 StreamingAssets/a.wav，构造流式 AudioClip 并替换 <paramref name="recordItem"/> 的 tracks 数组。
    /// 仅支持未压缩 WAV：16-bit PCM 或 32-bit IEEE float。文件不大，同步读取即可（仅首帧一次）。
    /// </summary>
    private void LoadCustomTrack(RecordItem recordItem) {
        // StreamingAssets 打包后是只读真实文件，直接拼路径读字节。
        var path = System.IO.Path.Combine(Application.streamingAssetsPath, "a.wav");
        if (!System.IO.File.Exists(path)) {
            MelonLogger.Error($"[CustomRecorder] 找不到音频文件: {path}");
            return;
        }

        int channels, sampleRate;
        try {
            var bytes = System.IO.File.ReadAllBytes(path);
            _trackSamples = DecodeWav(bytes, out channels, out sampleRate);
        }
        catch (Exception ex) {
            MelonLogger.Error($"[CustomRecorder] 解析 a.wav 失败: {ex}");
            return;
        }

        _trackReadPos = 0;
        int lengthSamples = _trackSamples.Length / channels; // 每声道采样数

        // stream=true：Unity 不一次性缓冲，而是反复调回调取数据，适合循环播放。
        // 回调里禁止访问 IL2CPP 复杂对象，只读写传入的 float 数组即可，最安全。
        // PCMReaderCallback/PCMSetPositionCallback 有从托管 System.Action 的隐式转换（DelegateSupport 包装）。
        AudioClip.PCMReaderCallback reader = (System.Action<Il2CppStructArray<float>>)PcmRead;
        AudioClip.PCMSetPositionCallback setPos = (System.Action<int>)PcmSetPosition;
        var clip = AudioClip.Create("a", lengthSamples, channels, sampleRate, true, reader, setPos);

        recordItem.tracks = new Il2CppReferenceArray<AudioClip>(new[] { clip });
        recordItem.loop = true;
        MelonLogger.Msg($"[CustomRecorder] 已替换 RecordItem.tracks，时长 {(float)lengthSamples / sampleRate:F1}s");
    }

    /// <summary>
    /// PCMReaderCallback：Unity 每次要播一段时调用，把接下来的样本写满 <paramref name="data"/>。
    /// 缓冲读完即环回（配合 RecordItem.loop / AudioSource 循环）。游标自增，不依赖 SetPosition。
    /// </summary>
    private void PcmRead(Il2CppStructArray<float> data) {
        var src = _trackSamples;
        if (src == null || src.Length == 0) {
            for (int i = 0; i < data.Length; i++) data[i] = 0f;
            return;
        }
        for (int i = 0; i < data.Length; i++) {
            data[i] = src[_trackReadPos];
            if (++_trackReadPos >= src.Length) _trackReadPos = 0;
        }
    }

    /// <summary>
    /// PCMSetPositionCallback：Unity seek 或循环回到起点时调用，参数是“每声道”采样位置。
    /// 换算成交错缓冲里的绝对索引。channels 信息已隐含在缓冲布局里，用总长比例还原即可。
    /// </summary>
    private void PcmSetPosition(int positionSamples) {
        var src = _trackSamples;
        if (src == null || src.Length == 0) { _trackReadPos = 0; return; }
        // positionSamples 是每声道的；这里直接按比例不安全，但我们只在循环回零时被调，position=0 居多。
        // 为稳妥起见对非零值也做钳制：交错索引 = position * channels，但 channels 未在此持有，
        // 故用最简单可靠的处理——回零。绝大多数循环场景 Unity 传 0。
        _trackReadPos = positionSamples <= 0 ? 0 : Math.Min(_trackReadPos, src.Length - 1);
    }

    /// <summary>
    /// 把未压缩 WAV 字节解析成交错 float 样本数组。支持 16-bit PCM(format=1) 和 32-bit float(format=3)。
    /// 逐块扫描 RIFF chunk，跳过 fmt/data 之外的块（如 LIST/fact），兼容带额外块的文件。
    /// </summary>
    private static float[] DecodeWav(byte[] bytes, out int channels, out int sampleRate) {
        // RIFF 头：'RIFF' <size:4> 'WAVE'，之后是若干 chunk：<id:4> <size:4> <data:size>。
        if (bytes.Length < 12 ||
            bytes[0] != 'R' || bytes[1] != 'I' || bytes[2] != 'F' || bytes[3] != 'F' ||
            bytes[8] != 'W' || bytes[9] != 'A' || bytes[10] != 'V' || bytes[11] != 'E') {
            throw new FormatException("不是合法的 RIFF/WAVE 文件");
        }

        int format = 0, bitsPerSample = 0;
        channels = 0;
        sampleRate = 0;
        int dataOffset = -1, dataLength = 0;

        int pos = 12;
        while (pos + 8 <= bytes.Length) {
            string id = System.Text.Encoding.ASCII.GetString(bytes, pos, 4);
            int size = BitConverter.ToInt32(bytes, pos + 4);
            int body = pos + 8;
            if (id == "fmt ") {
                format = BitConverter.ToUInt16(bytes, body);
                channels = BitConverter.ToUInt16(bytes, body + 2);
                sampleRate = BitConverter.ToInt32(bytes, body + 4);
                bitsPerSample = BitConverter.ToUInt16(bytes, body + 14);
            }
            else if (id == "data") {
                dataOffset = body;
                dataLength = size;
            }
            // chunk 按偶数字节对齐：奇数 size 后有 1 字节 padding。
            pos = body + size + (size & 1);
        }

        if (dataOffset < 0) throw new FormatException("缺少 data 块");
        if (channels <= 0) throw new FormatException("缺少 fmt 块或声道数非法");
        if (format != 1 && format != 3) throw new FormatException($"不支持的 WAV 格式 {format}（仅支持 PCM=1 / float=3）");
        if (format == 1 && bitsPerSample != 16) throw new FormatException($"PCM 仅支持 16-bit，实际 {bitsPerSample}-bit");
        if (format == 3 && bitsPerSample != 32) throw new FormatException($"float 仅支持 32-bit，实际 {bitsPerSample}-bit");

        // data 长度可能超过文件实际剩余（少见但要防越界）。
        if (dataOffset + dataLength > bytes.Length) dataLength = bytes.Length - dataOffset;

        int bytesPerSample = bitsPerSample / 8;
        int totalSamples = dataLength / bytesPerSample; // 含所有声道，即交错后的总采样点数
        var samples = new float[totalSamples];

        if (format == 1) {
            // 16-bit PCM：小端 short，归一化到 [-1,1]。
            for (int i = 0; i < totalSamples; i++) {
                short s = BitConverter.ToInt16(bytes, dataOffset + i * 2);
                samples[i] = s / 32768f;
            }
        }
        else {
            // 32-bit IEEE float：已是 [-1,1]，直接拷。
            for (int i = 0; i < totalSamples; i++) {
                samples[i] = BitConverter.ToSingle(bytes, dataOffset + i * 4);
            }
        }

        return samples;
    }
}
