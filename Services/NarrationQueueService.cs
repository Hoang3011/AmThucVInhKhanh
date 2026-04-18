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
    private static readonly TimeSpan TtsHardTimeout = TimeSpan.FromSeconds(120);

    private sealed class NarrationJob
    {
        public int PoiIndex { get; init; } = -1;
        /// <summary>Khi có Id từ CMS, chỉ thử file <c>poi_{Id}.mp3</c> — không dùng index list (thứ tự API có thể khác máy bundle).</summary>
        public int? BundledAudioPlaceId { get; init; }
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
    /// <param name="bundledAudioPlaceId">Id POI từ CMS; khi &gt; 0, chỉ tìm <c>poi_{Id}.mp3</c> trong gói app (không dùng chỉ số trong list).</param>
    public static Task<double?> EnqueuePoiOrTtsAsync(
        int poiIndex,
        string lang,
        string ttsFallbackText,
        CancellationToken cancellationToken = default,
        int? bundledAudioPlaceId = null)
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
            BundledAudioPlaceId = bundledAudioPlaceId,
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

        if (job.PoiIndex >= 0 || job.BundledAudioPlaceId is > 0)
            duration = await TryPlayPreRecordedPoiAudioAsync(job.PoiIndex, job.BundledAudioPlaceId, job.Lang, ct)
                .ConfigureAwait(false);

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

    private static IEnumerable<string> PreRecordedAssetCandidates(int poiIndex, int? bundledPlaceId)
    {
        if (bundledPlaceId is > 0)
        {
            var id = bundledPlaceId.Value;
            yield return $"audio/vietnamese/poi_{id}.mp3";
            yield return $"audio/vi/poi_{id}.mp3";
            yield return $"audio/poi_{id}.mp3";
            yield break;
        }

        if (poiIndex < 0)
            yield break;

        yield return $"audio/vietnamese/poi_{poiIndex}.mp3";
        yield return $"audio/vi/poi_{poiIndex}.mp3";
        yield return $"audio/poi_{poiIndex}.mp3";
    }

    private static async Task<double?> TryPlayPreRecordedPoiAudioAsync(
        int poiIndex,
        int? bundledPlaceId,
        string lang,
        CancellationToken cancellationToken)
    {
        if (!IsVietnameseLanguage(lang))
            return null;

        if (bundledPlaceId is not > 0 && poiIndex < 0)
            return null;

        foreach (var assetPath in PreRecordedAssetCandidates(poiIndex, bundledPlaceId))
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

    private static Locale? PickLocaleByPriority(Locale[] locales, params string[] languagePrefixes)
    {
        foreach (var prefix in languagePrefixes)
        {
            var hit = locales.FirstOrDefault(l => LocaleLanguageStartsWith(l, prefix));
            if (hit is not null)
                return hit;
        }

        return null;
    }

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
            // Android thường báo tiếng Trung là zh*, cmn* (Mandarin), yue* (Cantonese) — chỉ "zh" dễ null → im tiếng.
            var pick = langLower switch
            {
                "en" => PickLocaleByPriority(localeList, "en"),
                "zh" => PickLocaleByPriority(localeList, "zh", "cmn", "yue"),
                "ja" => PickLocaleByPriority(localeList, "ja"),
                _ => PickLocaleByPriority(localeList, "vi")
            };

            // Chế độ strict theo ngôn ngữ người dùng đã chọn:
            // không tự rơi sang ngôn ngữ khác vì dễ gây "chọn A đọc B".
            if (pick is null)
                return false;

            return await SpeakWithTimeoutAsync(
                text,
                new SpeechOptions
                {
                    Locale = pick,
                    Pitch = 1.0f,
                    Volume = 1.0f
                },
                cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"SpeakAsync: {ex.Message}");
            return false;
        }
    }

    private static async Task<bool> SpeakWithTimeoutAsync(string text, SpeechOptions? options, CancellationToken cancellationToken)
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        linked.CancelAfter(TtsHardTimeout);

        try
        {
            await TextToSpeech.Default.SpeakAsync(text, options, linked.Token).ConfigureAwait(false);
            return true;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            // Timeout nội bộ, coi như phát thất bại để caller fallback/tiếp tục lượt sau.
            return false;
        }
    }
}
