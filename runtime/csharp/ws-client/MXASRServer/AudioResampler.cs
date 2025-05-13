using System;
using System.IO;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;
using NAudio.Codecs; // 添加解码器引用

namespace MXASRServer
{
    /// <summary>
    /// 音频重采样工具类，用于将音频重采样到特定采样率
    /// </summary>
    public class AudioResampler
    {
        /// <summary>
        /// 将WAV音频数据重采样到指定采样率
        /// </summary>
        /// <param name="inputData">输入的音频字节数据</param>
        /// <param name="targetSampleRate">目标采样率，如16000</param>
        /// <returns>重采样后的音频数据</returns>
        public static byte[] ResampleWavData(byte[] inputData, int targetSampleRate)
        {
            using (var ms = new MemoryStream(inputData))
            {
                try
                {
                    // 尝试读取WAV文件
                    using (var reader = new WaveFileReader(ms))
                    {
                        int sourceSampleRate = reader.WaveFormat.SampleRate;
                        int channels = reader.WaveFormat.Channels;
                        int bitsPerSample = reader.WaveFormat.BitsPerSample;

                        Console.WriteLine($"WAV格式信息 - 通道数: {channels}, 位深: {bitsPerSample}, 采样率: {sourceSampleRate}, 编码: {reader.WaveFormat.Encoding}");

                        // 首先检查是否是PCM格式，如果不是，先转换为PCM
                        if (reader.WaveFormat.Encoding != WaveFormatEncoding.Pcm)
                        {
                            Console.WriteLine($"转换非PCM格式: {reader.WaveFormat.Encoding} 到 PCM");

                            // 特殊处理ALaw和MuLaw格式
                            if (reader.WaveFormat.Encoding == WaveFormatEncoding.ALaw ||
                                reader.WaveFormat.Encoding == WaveFormatEncoding.MuLaw)
                            {
                                return ConvertALawMuLawToPcm(inputData, targetSampleRate);
                            }

                            // 使用临时文件进行格式转换（兼容跨平台）
                            string tempInputFile = Path.GetTempFileName();
                            string tempConvertedFile = Path.GetTempFileName();

                            try
                            {
                                // 保存原始数据到临时文件
                                File.WriteAllBytes(tempInputFile, inputData);

                                // 使用AudioFileReader和WaveFormatConversionStream进行转换
                                using (var audioFileReader = new AudioFileReader(tempInputFile))
                                {
                                    // 创建PCM格式
                                    WaveFormat pcmFormat = new WaveFormat(
                                        audioFileReader.WaveFormat.SampleRate,
                                        16, // 16位
                                        audioFileReader.WaveFormat.Channels);

                                    // 从音频文件转换为PCM
                                    using (var pcmStream = new WaveFormatConversionStream(pcmFormat, audioFileReader))
                                    {
                                        WaveFileWriter.CreateWaveFile(tempConvertedFile, pcmStream);
                                    }
                                }

                                // 读取转换后的PCM数据
                                byte[] pcmData = File.ReadAllBytes(tempConvertedFile);

                                // 递归调用自身，此时已经是PCM格式
                                return ResampleWavData(pcmData, targetSampleRate);
                            }
                            finally
                            {
                                // 清理临时文件
                                try
                                {
                                    if (File.Exists(tempInputFile)) File.Delete(tempInputFile);
                                    if (File.Exists(tempConvertedFile)) File.Delete(tempConvertedFile);
                                }
                                catch { /* 忽略删除失败 */ }
                            }
                        }

                        // 然后检查采样率
                        // 如果源采样率是8K或16K，且目标采样率也是8K或16K，则直接返回原始数据
                        if ((sourceSampleRate == 8000 || sourceSampleRate == 16000) &&
                            (targetSampleRate == 8000 || targetSampleRate == 16000))
                        {
                            Console.WriteLine($"源采样率({sourceSampleRate})是8K或16K，目标采样率({targetSampleRate})也是8K或16K，无需重采样");
                            return inputData;
                        }

                        // 检查采样率是否需要转换
                        if (reader.WaveFormat.SampleRate == targetSampleRate)
                        {
                            // 采样率已经正确且是PCM格式，直接返回原始数据
                            return inputData;
                        }

                        // 使用跨平台的重采样方法
                        try
                        {
                            // 获取音频样本
                            ISampleProvider sourceProvider = reader.ToSampleProvider();

                            // 创建重采样器 (WdlResamplingSampleProvider是跨平台的)
                            var resampledProvider = new WdlResamplingSampleProvider(sourceProvider, targetSampleRate);

                            // 转换为16位PCM波形
                            var pcmProvider = new SampleToWaveProvider16(resampledProvider);

                            // 写入输出流
                            using (var outputStream = new MemoryStream())
                            {
                                using (var writer = new WaveFileWriter(outputStream, pcmProvider.WaveFormat))
                                {
                                    // 创建缓冲区
                                    byte[] buffer = new byte[4096];
                                    int bytesRead;

                                    // 读取重采样后的音频并写入输出流
                                    while ((bytesRead = pcmProvider.Read(buffer, 0, buffer.Length)) > 0)
                                    {
                                        writer.Write(buffer, 0, bytesRead);
                                    }
                                }

                                // 返回重采样后的数据
                                return outputStream.ToArray();
                            }
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine($"跨平台重采样失败，尝试使用临时文件: {ex.Message}");

                            // 备用方法：使用临时文件进行重采样
                            string tempInputFile = Path.GetTempFileName();
                            string tempOutputFile = Path.GetTempFileName();

                            try
                            {
                                // 保存数据到临时文件
                                File.WriteAllBytes(tempInputFile, inputData);

                                // 使用ResampleWavFile方法（已经实现了跨平台处理）
                                if (ResampleWavFile(tempInputFile, tempOutputFile, targetSampleRate))
                                {
                                    return File.ReadAllBytes(tempOutputFile);
                                }
                                else
                                {
                                    // 如果重采样失败，返回原始数据
                                    return inputData;
                                }
                            }
                            finally
                            {
                                // 清理临时文件
                                try
                                {
                                    if (File.Exists(tempInputFile)) File.Delete(tempInputFile);
                                    if (File.Exists(tempOutputFile)) File.Delete(tempOutputFile);
                                }
                                catch { /* 忽略删除失败 */ }
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"WAV文件处理失败: {ex.Message}");
                    // 如果处理失败，返回原始数据
                    return inputData;
                }
            }
        }

        /// <summary>
        /// 手动将ALaw或MuLaw格式转换为PCM并重采样
        /// </summary>
        private static byte[] ConvertALawMuLawToPcm(byte[] inputData, int targetSampleRate)
        {
            try
            {
                // 读取原始WAV头部信息
                if (inputData.Length < 44)
                {
                    Console.WriteLine("音频数据太短，无法读取WAV头");
                    return inputData;
                }

                // 读取格式
                byte[] formatBytes = new byte[2];
                Array.Copy(inputData, 20, formatBytes, 0, 2);
                short formatCode = BitConverter.ToInt16(formatBytes, 0);

                // 读取采样率
                byte[] sampleRateBytes = new byte[4];
                Array.Copy(inputData, 24, sampleRateBytes, 0, 4);
                int sourceSampleRate = BitConverter.ToInt32(sampleRateBytes, 0);

                // 读取通道数
                byte[] channelsBytes = new byte[2];
                Array.Copy(inputData, 22, channelsBytes, 0, 2);
                short channels = BitConverter.ToInt16(channelsBytes, 0);

                Console.WriteLine($"手动解析WAV - 格式: {formatCode}, 采样率: {sourceSampleRate}, 通道: {channels}");

                // 提取音频数据
                byte[] audioData = new byte[inputData.Length - 44];
                Array.Copy(inputData, 44, audioData, 0, audioData.Length);

                // 转换为PCM样本
                short[] pcmSamples;
                if (formatCode == 6) // ALaw
                {
                    Console.WriteLine("使用内置ALaw解码器进行转换");
                    pcmSamples = new short[audioData.Length];
                    for (int i = 0; i < audioData.Length; i++)
                    {
                        pcmSamples[i] = ALawDecoder.ALawToLinearSample(audioData[i]);
                    }
                }
                else if (formatCode == 7) // MuLaw
                {
                    Console.WriteLine("使用内置MuLaw解码器进行转换");
                    pcmSamples = new short[audioData.Length];
                    for (int i = 0; i < audioData.Length; i++)
                    {
                        pcmSamples[i] = MuLawDecoder.MuLawToLinearSample(audioData[i]);
                    }
                }
                else
                {
                    Console.WriteLine($"未知的格式代码: {formatCode}");
                    return inputData;
                }

                // 创建PCM格式
                WaveFormat pcmFormat = new WaveFormat(sourceSampleRate, 16, channels);

                // 创建内存流存储PCM数据
                using (var ms = new MemoryStream())
                {
                    using (var writer = new WaveFileWriter(ms, pcmFormat))
                    {
                        // 写入PCM样本
                        byte[] pcmBytes = new byte[pcmSamples.Length * 2]; // 16位 = 2字节/样本
                        Buffer.BlockCopy(pcmSamples, 0, pcmBytes, 0, pcmBytes.Length);
                        writer.Write(pcmBytes, 0, pcmBytes.Length);
                    }

                    // 获取PCM数据
                    byte[] pcmData = ms.ToArray();

                    // 如果需要重采样
                    if (sourceSampleRate != targetSampleRate)
                    {
                        Console.WriteLine($"对转换后的PCM数据进行重采样, 从 {sourceSampleRate}Hz 到 {targetSampleRate}Hz");
                        // 递归调用自身进行重采样，此时已是PCM格式
                        return ResampleWavData(pcmData, targetSampleRate);
                    }
                    else
                    {
                        return pcmData;
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"手动转换ALaw/MuLaw失败: {ex.Message}");
                return inputData;
            }
        }

        /// <summary>
        /// 检测WAV文件的采样率
        /// </summary>
        /// <param name="wavData">WAV文件数据</param>
        /// <returns>采样率</returns>
        public static int GetWavSampleRate(byte[] wavData)
        {
            try
            {
                using (var ms = new MemoryStream(wavData))
                using (var reader = new WaveFileReader(ms))
                {
                    return reader.WaveFormat.SampleRate;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"读取WAV采样率时出错: {ex.Message}，尝试手动读取");

                // 手动从WAV头部读取采样率
                if (wavData.Length >= 28)
                {
                    // 采样率存储在字节24-27
                    byte[] sampleRateBytes = new byte[4];
                    Array.Copy(wavData, 24, sampleRateBytes, 0, 4);
                    return BitConverter.ToInt32(sampleRateBytes, 0);
                }

                throw new ArgumentException("无法确定WAV文件的采样率");
            }
        }

        /// <summary>
        /// 检测WAV文件的编码格式
        /// </summary>
        /// <param name="wavData">WAV文件数据</param>
        /// <returns>编码格式</returns>
        public static WaveFormatEncoding GetWavEncoding(byte[] wavData)
        {
            try
            {
                using (var ms = new MemoryStream(wavData))
                using (var reader = new WaveFileReader(ms))
                {
                    return reader.WaveFormat.Encoding;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"读取WAV编码格式时出错: {ex.Message}，尝试手动读取");

                // 手动从WAV头部读取编码格式（字节20-21）
                if (wavData.Length >= 22)
                {
                    // 编码格式存储在字节20-21
                    byte[] formatBytes = new byte[2];
                    Array.Copy(wavData, 20, formatBytes, 0, 2);
                    short formatCode = BitConverter.ToInt16(formatBytes, 0);

                    // 1 = PCM, 6 = ALaw, 7 = MuLaw
                    switch (formatCode)
                    {
                        case 1: return WaveFormatEncoding.Pcm;
                        case 6: return WaveFormatEncoding.ALaw;
                        case 7: return WaveFormatEncoding.MuLaw;
                        default: return (WaveFormatEncoding)formatCode;
                    }
                }

                throw new ArgumentException("无法确定WAV文件的编码格式");
            }
        }

        /// <summary>
        /// 将WAV文件重采样到指定采样率
        /// </summary>
        /// <param name="inputFile">输入WAV文件路径</param>
        /// <param name="outputFile">输出WAV文件路径</param>
        /// <param name="targetSampleRate">目标采样率，如16000</param>
        /// <returns>是否成功</returns>
        public static bool ResampleWavFile(string inputFile, string outputFile, int targetSampleRate = 16000)
        {
            try
            {
                // 首先读取整个文件
                byte[] fileData = File.ReadAllBytes(inputFile);

                // 检查是否是ALaw或MuLaw格式
                WaveFormatEncoding encoding = GetWavEncoding(fileData);

                if (encoding == WaveFormatEncoding.ALaw || encoding == WaveFormatEncoding.MuLaw)
                {
                    Console.WriteLine($"检测到{encoding}格式，使用专用方法处理");

                    // 使用专用方法处理
                    byte[] processedData = ConvertALawMuLawToPcm(fileData, targetSampleRate);

                    // 写入输出文件
                    File.WriteAllBytes(outputFile, processedData);
                    return true;
                }

                // 对于其他格式，使用标准处理流程
                // 首先检查源文件的编码格式，优先处理格式问题
                int sourceSampleRate;

                using (var reader = new WaveFileReader(inputFile))
                {
                    encoding = reader.WaveFormat.Encoding;
                    sourceSampleRate = reader.WaveFormat.SampleRate;

                    Console.WriteLine($"WAV格式信息 - 采样率: {sourceSampleRate}, 编码: {encoding}");
                }

                // 如果不是PCM格式，首先转换为PCM
                if (encoding != WaveFormatEncoding.Pcm)
                {
                    Console.WriteLine($"检测到非PCM格式: {encoding}，首先转换为PCM");
                    string tempPcmFile = Path.GetTempFileName();

                    try
                    {
                        // 使用WaveFormatConversionStream转换为PCM
                        using (var sourceReader = new AudioFileReader(inputFile))
                        {
                            // 创建PCM格式 (16位立体声PCM)
                            WaveFormat pcmFormat = new WaveFormat(
                                sourceReader.WaveFormat.SampleRate,
                                16, // 16位
                                sourceReader.WaveFormat.Channels);

                            // 转换为PCM
                            using (var pcmStream = WaveFormatConversionStream.CreatePcmStream(sourceReader))
                            {
                                WaveFileWriter.CreateWaveFile(tempPcmFile, pcmStream);
                            }
                        }

                        // 递归调用自身，使用转换后的PCM文件
                        return ResampleWavFile(tempPcmFile, outputFile, targetSampleRate);
                    }
                    finally
                    {
                        // 清理临时文件
                        try
                        {
                            if (File.Exists(tempPcmFile)) File.Delete(tempPcmFile);
                        }
                        catch { /* 忽略删除失败 */ }
                    }
                }

                // 现在已经是PCM格式，检查采样率
                // 如果源采样率是8K或16K，且目标采样率也是8K或16K，则直接复制文件
                if ((sourceSampleRate == 8000 || sourceSampleRate == 16000) &&
                    (targetSampleRate == 8000 || targetSampleRate == 16000))
                {
                    Console.WriteLine($"源采样率({sourceSampleRate})是8K或16K，目标采样率({targetSampleRate})也是8K或16K，无需重采样");
                    File.Copy(inputFile, outputFile, true);
                    return true;
                }

                // 如果采样率已经符合要求，直接复制
                if (sourceSampleRate == targetSampleRate)
                {
                    File.Copy(inputFile, outputFile, true);
                    return true;
                }

                // 使用跨平台重采样方法 (AudioFileReader + WdlResamplingSampleProvider)
                try
                {
                    using (var audioFileReader = new AudioFileReader(inputFile))
                    {
                        // 创建重采样器
                        var resampler = new WdlResamplingSampleProvider(audioFileReader.ToSampleProvider(), targetSampleRate);

                        // 转换为16位PCM波形
                        var pcmProvider = new SampleToWaveProvider16(resampler);

                        // 创建输出文件
                        using (var writer = new WaveFileWriter(outputFile, pcmProvider.WaveFormat))
                        {
                            // 创建缓冲区
                            byte[] buffer = new byte[4096];
                            int bytesRead;

                            // 读取重采样后的音频并写入输出文件
                            while ((bytesRead = pcmProvider.Read(buffer, 0, buffer.Length)) > 0)
                            {
                                writer.Write(buffer, 0, bytesRead);
                            }
                        }

                        return true;
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"使用WdlResamplingSampleProvider重采样失败: {ex.Message}");

#if WINDOWS // MediaFoundationResampler 仅在 Windows 上可用
                    // Windows平台上的备用方法
                    using (var mediaReader = new MediaFoundationReader(inputFile))
                    {
                        // 创建目标格式
                        var targetFormat = new WaveFormat(
                            targetSampleRate,
                            mediaReader.WaveFormat.Channels,
                            mediaReader.WaveFormat.BitsPerSample);

                        // 使用MediaFoundationResampler
                        using (var resampler = new MediaFoundationResampler(mediaReader, targetFormat))
                        {
                            // 设置高质量重采样
                            resampler.ResamplerQuality = 60;
                            WaveFileWriter.CreateWaveFile(outputFile, resampler);
                        }

                        return true;
                    }
#else
                    // 非Windows平台上尝试使用其他方法
                    throw new PlatformNotSupportedException("当前平台不支持MediaFoundationResampler，无法进行重采样");
#endif
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"重采样WAV文件时出错: {ex.Message}");
                return false;
            }
        }
    }
}