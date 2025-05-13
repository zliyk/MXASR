using System;
using System.IO;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;

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
                        // 检查采样率是否需要转换
                        if (reader.WaveFormat.SampleRate == targetSampleRate)
                        {
                            // 如果输入采样率已经是目标采样率，则直接返回原始数据
                            return inputData;
                        }

                        // 获取WAV格式信息
                        int channels = reader.WaveFormat.Channels;
                        int bitsPerSample = reader.WaveFormat.BitsPerSample;
                        int sourceSampleRate = reader.WaveFormat.SampleRate;

                        Console.WriteLine($"WAV格式信息 - 通道数: {channels}, 位深: {bitsPerSample}, 采样率: {sourceSampleRate}");

                        // 确保是PCM格式
                        if (reader.WaveFormat.Encoding != WaveFormatEncoding.Pcm)
                        {
                            Console.WriteLine($"转换非PCM格式: {reader.WaveFormat.Encoding} 到 PCM");

                            // 先转换为PCM格式，再进行重采样
                            byte[] audioData = new byte[reader.Length];
                            ms.Position = 0;
                            ms.Read(audioData, 0, audioData.Length);

                            // 创建临时文件
                            string tempFile = Path.GetTempFileName();
                            string tempConvertedFile = Path.GetTempFileName();

                            try
                            {
                                // 写入临时文件
                                File.WriteAllBytes(tempFile, audioData);

                                // 使用MediaFoundationResampler转换
                                using (var mediaReader = new MediaFoundationReader(tempFile))
                                using (var pcmStream = WaveFormatConversionStream.CreatePcmStream(mediaReader))
                                {
                                    WaveFileWriter.CreateWaveFile(tempConvertedFile, pcmStream);
                                }

                                // 读取转换后的PCM文件
                                byte[] pcmData = File.ReadAllBytes(tempConvertedFile);

                                // 递归调用自身，此时已经是PCM格式
                                return ResampleWavData(pcmData, targetSampleRate);
                            }
                            finally
                            {
                                // 清理临时文件
                                try
                                {
                                    if (File.Exists(tempFile)) File.Delete(tempFile);
                                    if (File.Exists(tempConvertedFile)) File.Delete(tempConvertedFile);
                                }
                                catch { /* 忽略删除失败 */ }
                            }
                        }

                        try
                        {
                            // 标准PCM重采样路径
                            ISampleProvider sourceProvider = reader.ToSampleProvider();

                            // 创建重采样器
                            var resampledProvider = new WdlResamplingSampleProvider(
                                sourceProvider,
                                targetSampleRate);

                            // 将重采样后的音频转换为16位PCM格式
                            var pcmProvider = new SampleToWaveProvider16(resampledProvider);

                            // 写入输出流
                            using (var outputStream = new MemoryStream())
                            {
                                // 创建WAV文件写入器
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
                            Console.WriteLine($"标准重采样路径失败，尝试备用方法: {ex.Message}");

                            // 备用方法：使用MediaFoundationResampler
                            try
                            {
                                string tempFile = Path.GetTempFileName();
                                string tempResampledFile = Path.GetTempFileName();

                                try
                                {
                                    // 将数据写入临时文件
                                    File.WriteAllBytes(tempFile, inputData);

                                    // 使用MediaFoundationResampler进行重采样
                                    using (var mediaReader = new MediaFoundationReader(tempFile))
                                    using (var resampler = new MediaFoundationResampler(mediaReader, new WaveFormat(targetSampleRate, channels, bitsPerSample)))
                                    {
                                        // 设置高质量重采样
                                        resampler.ResamplerQuality = 60;
                                        WaveFileWriter.CreateWaveFile(tempResampledFile, resampler);
                                    }

                                    // 读取重采样后的文件
                                    return File.ReadAllBytes(tempResampledFile);
                                }
                                finally
                                {
                                    // 清理临时文件
                                    try
                                    {
                                        if (File.Exists(tempFile)) File.Delete(tempFile);
                                        if (File.Exists(tempResampledFile)) File.Delete(tempResampledFile);
                                    }
                                    catch { /* 忽略删除失败 */ }
                                }
                            }
                            catch (Exception innerEx)
                            {
                                Console.WriteLine($"备用重采样方法也失败: {innerEx.Message}");
                                throw new Exception($"无法重采样音频: {ex.Message}，备用方法: {innerEx.Message}", ex);
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"WAV文件处理失败: {ex.Message}");

                    // 尝试作为原始PCM数据处理
                    try
                    {
                        Console.WriteLine("尝试作为原始PCM数据处理...");

                        // 获取头部信息
                        if (inputData.Length >= 44)
                        {
                            byte[] sampleRateBytes = new byte[4];
                            Array.Copy(inputData, 24, sampleRateBytes, 0, 4);
                            int rawSampleRate = BitConverter.ToInt32(sampleRateBytes, 0);

                            byte[] channelsBytes = new byte[2];
                            Array.Copy(inputData, 22, channelsBytes, 0, 2);
                            short channels = BitConverter.ToInt16(channelsBytes, 0);

                            byte[] bitsPerSampleBytes = new byte[2];
                            Array.Copy(inputData, 34, bitsPerSampleBytes, 0, 2);
                            short bitsPerSample = BitConverter.ToInt16(bitsPerSampleBytes, 0);

                            Console.WriteLine($"从WAV头部手动读取 - 通道数: {channels}, 位深: {bitsPerSample}, 采样率: {rawSampleRate}");

                            // 创建WAV格式
                            WaveFormat waveFormat = new WaveFormat(rawSampleRate, bitsPerSample, channels);

                            // 提取PCM数据部分
                            byte[] pcmData = new byte[inputData.Length - 44];
                            Array.Copy(inputData, 44, pcmData, 0, pcmData.Length);

                            // 创建临时文件
                            string tempInFile = Path.GetTempFileName();
                            string tempOutFile = Path.GetTempFileName();

                            try
                            {
                                // 创建一个标准WAV文件
                                using (var fileStream = new FileStream(tempInFile, FileMode.Create))
                                using (var writer = new WaveFileWriter(fileStream, waveFormat))
                                {
                                    writer.Write(pcmData, 0, pcmData.Length);
                                }

                                // 使用MediaFoundationResampler进行重采样
                                using (var reader = new AudioFileReader(tempInFile))
                                using (var resampler = new MediaFoundationResampler(reader, new WaveFormat(targetSampleRate, channels, bitsPerSample)))
                                {
                                    resampler.ResamplerQuality = 60;
                                    WaveFileWriter.CreateWaveFile(tempOutFile, resampler);
                                }

                                // 读取重采样后的文件
                                return File.ReadAllBytes(tempOutFile);
                            }
                            finally
                            {
                                // 清理临时文件
                                try
                                {
                                    if (File.Exists(tempInFile)) File.Delete(tempInFile);
                                    if (File.Exists(tempOutFile)) File.Delete(tempOutFile);
                                }
                                catch { /* 忽略删除失败 */ }
                            }
                        }
                        else
                        {
                            Console.WriteLine("数据长度不足，无法获取WAV头部信息");
                        }
                    }
                    catch (Exception fallbackEx)
                    {
                        Console.WriteLine($"原始PCM处理也失败: {fallbackEx.Message}");
                    }

                    // 如果所有方法都失败，返回原始数据
                    Console.WriteLine("所有重采样方法都失败，返回原始数据");
                    return inputData;
                }
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
                // 读取整个文件
                byte[] fileData = File.ReadAllBytes(inputFile);

                // 重采样数据
                byte[] resampledData = ResampleWavData(fileData, targetSampleRate);

                // 写入输出文件
                File.WriteAllBytes(outputFile, resampledData);

                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"重采样WAV文件时出错: {ex.Message}");

                try
                {
                    // 备用方法：使用MediaFoundationResampler
                    using (var reader = new MediaFoundationReader(inputFile))
                    {
                        // 如果采样率已经符合要求，直接复制
                        if (reader.WaveFormat.SampleRate == targetSampleRate)
                        {
                            File.Copy(inputFile, outputFile, true);
                            return true;
                        }

                        // 创建目标格式
                        var targetFormat = new WaveFormat(
                            targetSampleRate,
                            reader.WaveFormat.Channels,
                            reader.WaveFormat.BitsPerSample);

                        // 使用MediaFoundationResampler
                        using (var resampler = new MediaFoundationResampler(reader, targetFormat))
                        {
                            // 设置高质量重采样
                            resampler.ResamplerQuality = 60;
                            WaveFileWriter.CreateWaveFile(outputFile, resampler);
                        }

                        return true;
                    }
                }
                catch (Exception backupEx)
                {
                    Console.WriteLine($"使用备用方法重采样WAV文件时出错: {backupEx.Message}");
                    return false;
                }
            }
        }
    }
}