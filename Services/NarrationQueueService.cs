using System.Threading.Channels;
using Microsoft.Maui.ApplicationModel;
using Microsoft.Maui.Media;
using Plugin.Maui.Audio;

namespace TourGuideApp2.Services;

/// <summary>
/// Hàng đợi thuyết minh: phát tuần tự (file POI trước nếu có, rồi TTS), một luồng duy nhất.
/// </summary>
public static class NarrationQueueService
{
    private sealed class NarrationJob
    {
        public int PoiIndex { get; init; } = -1;
        public string Lang { get; init; } = "vi";
        public string TtsFallbackText { get; init; } = "";
        public CancellationToken CancellationToken { get; init; }
        public TaskCompletionSource<double?> Completion { get; init; } = null!;
    }

    private static readonly Channel<NarrationJob> Jobs = Channel.CreateUnbounded<NarrationJob>(new UnboundedChannelOptions
    {
        SingleReader = true,
        SingleWriter = false
    });

    private static readonly object PlayerLock = new();
    private static IAudioPlayer? _player;

    static NarrationQueueService()
    {
        _ = Task.Run(ProcessLoopAsync);
    }

    /// <summary>Thử file âm thanh theo POI (chỉ tiếng Việt), không được thì TTS <paramref name="ttsFallbackText"/>.</summary>
    public static Task<double?> EnqueuePoiOrTtsAsync(int poiIndex, string lang, string ttsFallbackText, CancellationToken cancellationToken = default)
    {
        var tcs = new TaskCompletionSource<double?>(TaskCreationOptions.RunContinuationsAsynchronously);
        if (poiIndex < 0 && string.IsNullOrWhiteSpace(ttsFallbackText))
        {
            tcs.SetResult(null);
            return tcs.Task;
        }

        var job = new NarrationJob
        {
            PoiIndex = poiIndex,
            Lang = string.IsNullOrWhiteSpace(lang) ? "vi" : lang.Trim(),
            TtsFallbackText = ttsFallbackText ?? "",
            CancellationToken = cancellationToken,
            Completion = tcs
        };

        if (!Jobs.Writer.TryWrite(job))
        {
            // Không fault task — tránh unobserved exception trên máy chậm / nhiều tab.
            tcs.TrySetResult(null);
            return tcs.Task;
        }

        return tcs.Task;
    }

    /// <summary>Dừng file đang phát (TTS hủy qua <see cref="CancellationToken"/> của job).</summary>
    public static void StopActivePlayer()
    {
        lock (PlayerLock)
        {
            try
            {
                _player?.Stop();
            }
            catch
            {
                // Bỏ qua.
            }
        }
    }

    private static async Task ProcessLoopAsync()
    {
        await foreach (var job in Jobs.Reader.ReadAllAsync())
        {
            try
            {
                if (job.CancellationToken.IsCancellationRequested)
                {
                    job.Completion.TrySetCanceled(job.CancellationToken);
                    continue;
                }

                using var _ = job.CancellationToken.Register(StopActivePlayer);
                var duration = await PlayNarrationAsync(job);
                job.Completion.TrySetResult(duration);
            }
            catch (OperationCanceledException)
            {
                job.Completion.TrySetCanceled();
            }
            catch (Exception ex)
            {
                // Không fault task await — tránh crash khi TTS/audio lỗi trên một số máy (task không được observe).
                System.Diagnostics.Debug.WriteLine($"NarrationQueue: {ex.Message}");
                job.Completion.TrySetResult(null);
            }
        }
    }

    private static async Task<double?> PlayNarrationAsync(NarrationJob job)
    {
        var ct = job.CancellationToken;
        double? duration = null;

        if (job.PoiIndex >= 0)
            duration = await TryPlayPreRecordedPoiAudioAsync(job.PoiIndex, job.Lang, ct).ConfigureAwait(false);

        if (duration.HasValue)
            return duration;

        if (string.IsNullOrWhiteSpace(job.TtsFallbackText))
            return null;

        var startedAt = DateTime.UtcNow;
        var ok = await SpeakAsync(job.TtsFallbackText, job.Lang, ct).ConfigureAwait(false);
        if (!ok)
            return null;

        return (DateTime.UtcNow - startedAt).TotalSeconds;
    }

    private static bool IsVietnameseLanguage(string? lang)
    {
        return !string.IsNullOrWhiteSpace(lang)
               && lang.Trim().StartsWith("vi", StringComparison.OrdinalIgnoreCase);
    }

    private static async Task<double?> TryPlayPreRecordedPoiAudioAsync(int poiIndex, string lang, CancellationToken cancellationToken)
    {
        if (poiIndex < 0) return null;
        if (!IsVietnameseLanguage(lang)) return null;

        var candidates = new[]
        {
            $"audio/vietnamese/poi_{poiIndex}.mp3",
            $"audio/vi/poi_{poiIndex}.mp3",
            $"audio/poi_{poiIndex}.mp3"
        };

        foreach (var assetPath in candidates)
        {
            Stream? stream = null;
            try
            {
                stream = await FileSystem.OpenAppPackageFileAsync(assetPath).ConfigureAwait(false);
            }
            catch
            {
                continue;
            }

            await using (stream)
            {
                IAudioPlayer? player = null;
                try
                {
                    player = AudioManager.Current.CreatePlayer(stream);
                    lock (PlayerLock)
                    {
                        try
                        {
                            _player?.Stop();
                            _player?.Dispose();
                        }
                        catch
                        {
                            // Bỏ qua.
                        }

                        _player = player;
                    }

                    var startedAt = DateTime.UtcNow;
                    player.Play();
                    var playedAtLeastOnce = false;
                    while (player.IsPlaying)
                    {
                        playedAtLeastOnce = true;
                        cancellationToken.ThrowIfCancellationRequested();
                        await Task.Delay(100, cancellationToken).ConfigureAwait(false);
                    }

                    var elapsed = (DateTime.UtcNow - startedAt).TotalSeconds;
                    if (!playedAtLeastOnce || elapsed < 0.8)
                        return null;

                    return elapsed;
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"PreRecorded audio failed ({assetPath}): {ex.Message}");
                    continue;
                }
                finally
                {
                    lock (PlayerLock)
                    {
                        if (ReferenceEquals(_player, player))
                        {
                            try
                            {
                                _player?.Stop();
                                _player?.Dispose();
                            }
                            catch
                            {
                                // Bỏ qua.
                            }

                            _player = null;
                        }
                        else
                        {
                            try
                            {
                                player?.Dispose();
                            }
                            catch
                            {
                                // Bỏ qua.
                            }
                        }
                    }
                }
            }
        }

        return null;
    }

    private static bool LocaleLanguageStartsWith(Locale l, string prefix) =>
        !string.IsNullOrEmpty(l.Language) && l.Language.StartsWith(prefix, StringComparison.OrdinalIgnoreCase);

    /// <summary>Android: TTS/GetLocales an toàn hơn khi gọi trên luồng UI.</summary>
    private static async Task<bool> SpeakAsync(string text, string lang, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(text)) return false;
        cancellationToken.ThrowIfCancellationRequested();

        try
        {
            return await MainThread.InvokeOnMainThreadAsync(() => SpeakCoreOnUiAsync(text, lang, cancellationToken))
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"SpeakAsync (marshal UI): {ex.Message}");
            return false;
        }
    }

    private static async Task<bool> SpeakCoreOnUiAsync(string text, string lang, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        try
        {
            var locales = await TextToSpeech.Default.GetLocalesAsync().ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            var localeList = locales == null ? Array.Empty<Locale>() : locales.ToArray();
            var langLower = (lang ?? string.Empty).Trim().ToLowerInvariant();
            var isVi = IsVietnameseLanguage(langLower);

            Locale? pick = langLower switch
            {
                "en" => localeList.FirstOrDefault(l => LocaleLanguageStartsWith(l, "en")),
                "zh" => localeList.FirstOrDefault(l => LocaleLanguageStartsWith(l, "zh")),
                "ja" => localeList.FirstOrDefault(l => LocaleLanguageStartsWith(l, "ja")),
                _ => localeList.FirstOrDefault(l => LocaleLanguageStartsWith(l, "vi"))
            };

            if (!isVi)
                pick ??= localeList.FirstOrDefault();

            await TextToSpeech.Default.SpeakAsync(text, new SpeechOptions
            {
                Locale = pick,
                Pitch = 1.0f,
                Volume = 1.0f
            }, cancellationToken).ConfigureAwait(false);
            return true;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"SpeakAsync: {ex.Message}");
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                await TextToSpeech.Default.SpeakAsync(text, options: null, cancelToken: cancellationToken)
                    .ConfigureAwait(false);
                return true;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception innerEx)
            {
                System.Diagnostics.Debug.WriteLine($"SpeakAsync fallback: {innerEx.Message}");
                return false;
            }
        }
    }
}
