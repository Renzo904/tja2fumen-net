﻿using NAudio.Vorbis;
using NAudio.Wave.SampleProviders;
using NAudio.Wave;
using SimpleHelpers;
using SonicAudioLib.Archives;
using SonicAudioLib.CriMw;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.IO.Compression;
using VGAudio.Containers.Hca;
using VGAudio.Containers.Wave;
using VGAudio.Formats.Pcm16;
using VGAudio.Containers.Adx;
using VGAudio.Codecs.CriHca;
using VGAudio.Formats.CriHca;

namespace tja2fumen
{
    public class TJAConvert
    {
        public const int PaddedSongTime = 2 * 1000; // in ms
        public const float TjaOffsetForPaddingSong = -1.0f; // in ms

        public const ulong ns2HcaKey = 52539816150204134;

        public enum FileType
        {
            WAV,
            OGG,
            MP3,
            UNK
        }

        private static void Pack(string acbPath, string path)
        {
            const int bufferSize = 4096;
            

            if (!File.Exists(acbPath))
                throw new FileNotFoundException("Unable to locate the corresponding ACB file. Please ensure that it's in the same directory.");

            CriTable acbFile = new CriTable();
            acbFile.Load(acbPath, bufferSize);

            CriAfs2Archive afs2Archive = new CriAfs2Archive();

            CriCpkArchive cpkArchive = new CriCpkArchive();
            CriCpkArchive extCpkArchive = new CriCpkArchive();
            cpkArchive.Mode = extCpkArchive.Mode = CriCpkMode.Id;

            using (CriTableReader reader = CriTableReader.Create((byte[])acbFile.Rows[0]["WaveformTable"]))
            {
                while (reader.Read())
                {
                    ushort id = reader.ContainsField("MemoryAwbId") ? reader.GetUInt16("MemoryAwbId") : reader.GetUInt16("Id");

                    string inputName = id.ToString("D5");

                    inputName += ".hca";
                    inputName = Path.Combine(path, inputName);

                    if (!File.Exists(inputName))
                        throw new FileNotFoundException($"Unable to locate {inputName}");

                    CriAfs2Entry entry = new CriAfs2Entry
                    {
                        FilePath = new FileInfo(inputName),
                        Id = id
                    };
                    
                    afs2Archive.Add(entry);
                }
            }
            
            acbFile.Rows[0]["AwbFile"] = null;
            acbFile.Rows[0]["StreamAwbAfs2Header"] = null;

            if (afs2Archive.Count > 0 || cpkArchive.Count > 0)
                acbFile.Rows[0]["AwbFile"] = afs2Archive.Save();

            acbFile.WriterSettings = CriTableWriterSettings.Adx2Settings;
            
            acbFile.Save(acbPath, bufferSize);
        }

        public static bool ConvertToAcb(string filePath, FileType fileType, bool isPreview = false, int milisecondsOffset = 0)
        {
            try
            {
                var directory = Path.GetDirectoryName(filePath) ?? "";
                string fileName = Path.GetFileNameWithoutExtension(filePath) + ".acb";
                if (isPreview)
                {
                    fileName = "P" + fileName;
                }
                var acbPath = Path.Combine(directory, fileName);

                if (File.Exists(acbPath))
                {
                    return true;
                }
                string hcaPath = $"{directory}/00000.hca";

                File.WriteAllBytes(acbPath, Files.TemplateACBData);
                byte[]? hca = null;
                switch (fileType)
                {
                    case FileType.WAV:
                        hca = WavToHca(filePath, isPreview, milisecondsOffset);
                        break;

                    case FileType.OGG:
                        hca = OggToHca(filePath, isPreview, milisecondsOffset);
                        break;

                    case FileType.MP3:
                        hca = Mp3ToHca(filePath, isPreview, milisecondsOffset);
                        break;

                    case FileType.UNK:
                    default:
                        hca = null;
                        break;


                }
                
                if(hca == null)
                {
                    return false;
                }

                File.WriteAllBytes(hcaPath, hca);
                Pack(acbPath, directory);
                File.Delete(hcaPath);
                
                return true;
            }
            catch (Exception e)
            {
                Console.WriteLine(e);
                return false;
            }
        }

        private static byte[] ConvertToHca(SampleToWaveProvider16 wavProvider, bool isPreview, int milisecondsOffset = 0)
        {
            var memoryStream = new MemoryStream();

            TimeSpan delay = TimeSpan.Zero;
            TimeSpan skip = TimeSpan.Zero;
            TimeSpan take = TimeSpan.Zero;

            if (isPreview)
            {
                skip = TimeSpan.FromMilliseconds(milisecondsOffset);
                take = TimeSpan.FromMilliseconds(15000);
            }
            else
            {
                delay = TimeSpan.FromMilliseconds(milisecondsOffset);
            }
            
            if (milisecondsOffset > 0 || isPreview)
            {
                var trimmed = new OffsetSampleProvider(wavProvider.ToSampleProvider())
                {
                    DelayBy = delay,
                    SkipOver = skip,
                    Take = take
                };
                
                WaveFileWriter.WriteWavFileToStream(memoryStream, trimmed.ToWaveProvider16());
            }
            else
            {
                WaveFileWriter.WriteWavFileToStream(memoryStream, wavProvider);
            }
            var hcaWriter = new HcaWriter();
            hcaWriter.Configuration.EncryptionKey = new CriHcaKey(ns2HcaKey);

            var waveReader = new WaveReader();
            var audioData = waveReader.Read(memoryStream.ToArray());

            var encodingConfig = new CriHcaParameters
            {
                Progress = hcaWriter.Configuration.Progress,
                Bitrate = hcaWriter.Configuration.Bitrate,
                LimitBitrate = hcaWriter.Configuration.LimitBitrate
            };
            if (hcaWriter.Configuration.Quality != CriHcaQuality.NotSet)
            {
                encodingConfig.Quality = hcaWriter.Configuration.Quality;
            }

            var hcaFormat = audioData.GetFormat<CriHcaFormat>(encodingConfig);
            var Hca = hcaFormat.Hca;
            HcaEncryption.CriHcaEncryption.Crypt(Hca, hcaFormat.AudioData, hcaWriter.Configuration.EncryptionKey, false);


            return hcaWriter.GetFile(audioData, hcaWriter.Configuration);
        }

        private static byte[] WavToHca(string path, bool isPreview = false, int milisecondsOffset = 0)
        {
            
            WaveFileReader reader = new WaveFileReader(path);
            var wavProvider = new SampleToWaveProvider16(reader.ToSampleProvider());
            return ConvertToHca(wavProvider, isPreview, milisecondsOffset);
        }

        private static byte[] OggToHca(string inPath, bool isPreview = false, int milisecondsOffset = 0)
        {
            try
            {
                using FileStream fileIn = new FileStream(inPath, FileMode.Open);
                var vorbis = new VorbisWaveReader(fileIn);
                
                var wavProvider = new SampleToWaveProvider16(vorbis.ToSampleProvider());
                return ConvertToHca(wavProvider, isPreview, milisecondsOffset);
            }
            catch (Exception e)
            {
                Console.WriteLine(e);
                return null;
            }
        }


        private static byte[] Mp3ToHca(string inPath, bool isPreview = false, int milisecondsOffset = 0)
        {
            try
            {
                using FileStream fileIn = new FileStream(inPath, FileMode.Open);
                var mp3 = new Mp3FileReader(fileIn);
                
                
                var wavProvider = new SampleToWaveProvider16(mp3.ToSampleProvider());
                return ConvertToHca(wavProvider, isPreview, milisecondsOffset);
            }
            catch (Exception e)
            {
                Console.WriteLine(e);
                return null;
            }
        }
    }
}
