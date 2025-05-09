using System.Text;
using vkbot_vitalya.Config;
using vkbot_vitalya.Core;
using vkbot_vitalya.Core.Saver;
using vkbot_vitalya.Services;
using VkNet.Exception;
using VkNet.Model;

namespace vkbot_vitalya;
/* todo
 Кеширование пикч в альбоме/вк/локально
 Выход за 1000 страниц
 Адаптация для юзербота
 Добавить другие источники пикч
 Возможность запуска с ведройда
 Прочекать логи на ошибки - там много необработанных сетевых исключений и ошибок вкалтакте
 Переход на обертку вк/тг апи
 */
public static class Program {
    private static MessageHandler _handler;
    private static MessageSaver _messageSaver;
    private static Bot _bot = new Bot();

    public static string _savedMessagesFolder = Path.Combine(Environment.CurrentDirectory, "SavedMessages");

    private static ManualResetEvent _shutdownEvent = new ManualResetEvent(false);

    public static void Main(string[] args) {
        Console.OutputEncoding = Encoding.Unicode;
        Console.ForegroundColor = ConsoleColor.White;

        L.I($"Bot started at {DateTime.Now}");

        if (!Conf.Instance.UseForbiddenTags) {
            L.W("FORBIDDEN TAGS IGNORED");
        }

        _handler = new MessageHandler(_bot);
        _messageSaver = new MessageSaver(_savedMessagesFolder);


        try {
            _bot.Api.Authorize(new ApiAuthParams { AccessToken = Auth.Instance.AccessToken });
            L.I("Authorization successful");
        } catch (Exception ex) {
            L.I($"Authorization failed: {ex.Message}");
            return;
        }

        SetupSignalHandling();
        try {
            var longPollServer = _bot.Api.Groups.GetLongPollServer(Auth.Instance.GroupId);
            StartLongPoll(longPollServer);
        } catch (Exception ex) {
            L.I($"Error in long poll: {ex.Message}");
            return;
        }

        _shutdownEvent.WaitOne();
        L.I("Saving cache...");
        DanbooruApi.SaveCache();
        SafebooruApi.SaveCache();
        L.I($"Bot stopped at {DateTime.Now}");
    }

    private static async Task StartLongPoll(LongPollServerResponse longPollServer) {
        var lastUpdateTs = longPollServer.Ts;

        while (true) {
            try {
                var poll = await _bot.Api.Groups.GetBotsLongPollHistoryAsync(
                    new BotsLongPollHistoryParams {
                        Server = longPollServer.Server,
                        Ts = lastUpdateTs,
                        Key = longPollServer.Key,
                        Wait = 25
                    });

                if (poll?.Updates == null) continue;

                foreach (var update in poll.Updates) {
                    if (update.Instance is MessageNew messageNew) {
                        var message = messageNew.Message;

                        var needSave = !ExceptDict.Get().Any(message.Text.StartsWith);

                        // Save message to file
                        if (needSave && _messageSaver != null)
                            await _messageSaver.SaveMessage(message);

                        try {
                            await _handler.HandleMessage(message);
                        } catch (Exception e) {
                            L.E("Failed to handle message", e);
                        }
                    }
                }

                // Update Ts for the next request
                lastUpdateTs = poll.Ts;
            } catch (LongPollKeyExpiredException) {
                // Refresh longPollServer for correct work
                longPollServer = _bot.Api.Groups.GetLongPollServer(Auth.Instance.GroupId);
            } catch (Exception e) {
                L.E("LongPoll error", e);
            }
        }
    }

    private static void SetupSignalHandling() {
        Console.CancelKeyPress += (sender, e) => {
            e.Cancel = true;
            _shutdownEvent.Set();
        };

        AppDomain.CurrentDomain.ProcessExit += (sender, e) => _shutdownEvent.Set();
    }
}