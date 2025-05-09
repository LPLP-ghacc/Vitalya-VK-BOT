using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using vkbot_vitalya.Config;
using vkbot_vitalya.Core;
using vkbot_vitalya.Services;
using vkbot_vitalya.Services.Generators;
using vkbot_vitalya.Services.Generators.TextGeneration;
using VkNet.Enums.StringEnums;
using VkNet.Exception;
using VkNet.Model;
using Image = SixLabors.ImageSharp.Image;

namespace vkbot_vitalya;

/// <summary>
/// Extention to handlers
/// </summary>
public partial class MessageHandler {
    private Dictionary<long, int> chaosScores = new Dictionary<long, int>(); // Счёт хаоса

    private async Task HandleImageCommand(Message message, Func<Image<Rgba32>, Image<Rgba32>> imageProcessor) {
        var originalImage = await FindImageInMessage(message);
        if (originalImage == null) {
            L.W("HandleImageCommand: image not found");
            return;
        }

        try {
            Image<Rgba32> processedImage;
            try {
                processedImage = imageProcessor(originalImage);
            } catch (Exception e) {
                L.E($"Error processing image: {e.Message}");
                return;
            }

            var photo = await _bot.UploadImage(processedImage);
            if (photo == null) {
                return;
            }

            // Send the saved photo in a message
            _bot.Api.Messages.Send(new MessagesSendParams {
                RandomId = Rand.Next(),
                PeerId = message.PeerId,
                ReplyTo = message.Id,
                Attachments = [photo]
            });

            L.I("Processed photo sent to user.");
        } catch (Exception e) {
            L.E("Failed to handle command", e);
        }
    }

    private async Task HandleMemeCommand(Message message, string alias, string args) {
        Meme? meme;
        if (string.IsNullOrEmpty(args)) {
            // Генерируем случайный мем, если ключевые слова отсутствуют
            meme = await ServiceEndpoint.MemeGen.RandomMeme(string.Empty, MemeType.Image);
        } else {
            // Генерируем мем по введенным ключевым словам
            meme = await ServiceEndpoint.MemeGen.RandomMeme(args, MemeType.Image);
        }

        if (meme != null) {
            var memeUrl = meme.Url;
            L.I($"Found meme URL: {memeUrl}");

            try {
                var photo = await _bot.UploadImageFrom(memeUrl, new HttpClient());
                if (photo == null) {
                    return;
                }

                var text = await MessageProcessor.KeepUpConversation();

                // Send the saved meme image in a message
                _bot.Api.Messages.Send(new MessagesSendParams {
                    RandomId = Rand.Next(),
                    PeerId = message.PeerId,
                    ReplyTo = message.Id,
                    Attachments = [photo],
                    Message = text
                });

                L.I("Meme sent to user.");
            } catch (Exception ex) {
                L.E($"Exception in HandleMemeCommand: {ex.Message}");
                L.E($"Stack Trace: {ex.StackTrace}");
            }
        } else {
            L.I("No meme found.");
            Answer(message, "Извините, не удалось найти мемы по заданным ключевым словам.");
        }
    }

    private async Task HandleWeatherCommand(Message message, string alias, string args) {
        var weatherResponse = await ServiceEndpoint.WeatherService.GetWeatherAsync(args);
        if (weatherResponse != null) {
            var weatherMessage =
                $"Погода в {weatherResponse.Name}:\n" +
                $"Температура: {weatherResponse.Main?.Temp ?? 0}°C\n" +
                $"Ощущается как: {weatherResponse.Main?.FeelsLike ?? 0}°C\n" +
                $"Описание: {weatherResponse.Weather?.FirstOrDefault()?.Description}\n" +
                $"Ветер: {weatherResponse.Wind?.Speed ?? 0} м/с\n" +
                $"Влажность: {weatherResponse.Main?.Humidity ?? 0}%";

            Answer(message, weatherMessage, message.Id);
        } else {
            Answer(message, "Извините, не удалось получить информацию о погоде.", message.Id);
        }
    }

    private async Task HandleAnimeCommand(Message message, string alias, string tags) {
        L.I($"Requesting Safebooru with tags: {tags}");

        var (post, err) = await ServiceEndpoint.SafebooruApi.RandomPostAsync(tags);

        if (post == null) {
            L.I("No anime image found.");
            Answer(message, err + ".");
            return;
        }

        var mediaUrl = post.FileUrl;
        L.I($"Found media URL: {mediaUrl}");

        try {
            MediaAttachment? attch;
            var mediaType = mediaUrl.Split('.')[^1] == "gif" ? "doc" : "photo";
            if (SafebooruApi.MediaCache.TryGetValue(post.Id, out var cache)) {
                /* Отправка медиа из сохр */
                attch = mediaType == "photo" ? new Photo() : new Document();
                attch.OwnerId = cache.VkOwnerId;
                attch.Id = cache.VkMediaId;
                attch.AccessKey = cache.VkAccessKey;
            } else {
                /* Загрузка медиа в вк */
                attch = mediaType == "photo"
                    ? await _bot.UploadImageFrom(mediaUrl, ServiceEndpoint.SafebooruApi.HttpClient)
                    : await _bot.UploadGifFrom(mediaUrl, message.PeerId, ServiceEndpoint.SafebooruApi.HttpClient);
                if (attch == null) return;
                string vkMediaUrl;
                vkMediaUrl = attch is Photo photo
                    ? photo.Sizes.OrderByDescending(size => size.Height).First().Url.AbsoluteUri
                    : (attch as Document)!.Uri;
                /* Один пост может грузиться дважды, если посты зациклились быстрее чем загрузились */
                SafebooruApi.MediaCache.TryAdd(post.Id,
                    new(attch.OwnerId!.Value, attch.Id!.Value, attch.AccessKey, vkMediaUrl, ""));
            }

            List<string> variableLabel = [
                "Еще!",
                "Ещ...е.. а.",
                "Ах!! !!",
                "Еще!",
                "Еще!",
                "Еще!",
                "Еще!",
                "Еще!",
                "Еще!",
                "Еще!",
                "Еще!",
                "Еще!",
                "Еще!",
                "Еще!",
                "Еще!",
                "Еще!",
                "Еще!",
                "Еще!",
                "ещо"
            ];
            
            var b = new MessageKeyboardButton {
                Action = new MessageKeyboardButtonAction {
                    Type = KeyboardButtonActionType.Text,
                    Label = variableLabel[Rand.Next(variableLabel.Count)],
                    Payload = JsonConvert.SerializeObject(new { command = "anim", _tags = tags })
                }
            };

            List<MessageKeyboardButton> buttonsRow1 = [b];

            var values = new List<List<MessageKeyboardButton>> { buttonsRow1 };

            var keyboard = new MessageKeyboard {
                Buttons = values,
                Inline = true
            };

            _bot.Api.Messages.Send(new MessagesSendParams {
                RandomId = Rand.Next(),
                PeerId = message.PeerId,
                Attachments = [attch],
                ReplyTo = message.Id,
                Keyboard = keyboard
            });

            L.I("Anime image sent to user.");
        } catch (Exception e) {
            L.E($"Exception in HandleAnimeCommand", e);
        }
    }

    private async Task HandleHCommand(Message message, string alias, string tags) {
        L.I($"Requesting Danbooru with tags: {tags}");

        string? mediaUrl, err;
        DanbooruPost? post;
        try {
            (post, err) = await ServiceEndpoint.DanbooruApi.RandomPostAsync(tags);
        } catch (Exception e) {
            L.E("Failed to find XXX", e);
            Answer(message, "Что-то пошло не так, попробуйте позже");
            return;
        }

        if (post == null) {
            L.I("No anime image found.");
            Answer(message, err + ".");
            return;
        }

        mediaUrl = post.FileUrl!;

        L.I($"Found image URL: {mediaUrl}");

        try {
            MediaAttachment? attch;
            var mediaType = mediaUrl.Split('.')[^1] == "gif" ? "doc" : "photo";
            if (DanbooruApi.MediaCache.TryGetValue(post.Id, out var cache)) {
                /* Отправка медиа из сохр */
                attch = mediaType == "photo" ? new Photo() : new Document();
                attch.OwnerId = cache.VkOwnerId;
                attch.Id = cache.VkMediaId;
                attch.AccessKey = cache.VkAccessKey;
            } else {
                /* Загрузка медиа в вк */
                attch = mediaType == "photo"
                    ? await _bot.UploadImageFrom(mediaUrl, ServiceEndpoint.DanbooruApi.HttpClient)
                    : await _bot.UploadGifFrom(mediaUrl, message.PeerId, ServiceEndpoint.DanbooruApi.HttpClient);
                if (attch == null) return;
                string vkMediaUrl;
                vkMediaUrl = attch is Photo photo
                    ? photo.Sizes.OrderByDescending(size => size.Height).First().Url.AbsoluteUri
                    : (attch as Document)!.Uri;
                /* Один пост может грузиться дважды, если посты зациклились быстрее чем загрузились */
                DanbooruApi.MediaCache.TryAdd(post.Id,
                    new(attch.OwnerId!.Value, attch.Id!.Value, attch.AccessKey, vkMediaUrl, ""));
            }
            
            var b = new MessageKeyboardButton {
                Action = new MessageKeyboardButtonAction {
                    Type = KeyboardButtonActionType.Text,
                    Label = "Еще!",
                    Payload = JsonConvert.SerializeObject(new { command = "hen", _tags = tags })
                }
            };

            List<MessageKeyboardButton> buttonsRow1 = [b];

            var values = new List<List<MessageKeyboardButton>> { buttonsRow1 };

            var keyboard = new MessageKeyboard {
                Buttons = values,
                Inline = true
            };

            _bot.Api.Messages.Send(new MessagesSendParams {
                RandomId = Rand.Next(),
                PeerId = message.PeerId,
                Attachments = [attch],
                ReplyTo = message.Id,
                Keyboard = keyboard
            });

            L.I("Anime image sent to user.");
        } catch (Exception e) {
            L.E("Exception in HandleAnimeCommand", e);
        }
    }

    private async Task HandleHelpCommand(Message message, string alias, string args) {
        var help = Regex.Replace(File.ReadAllText("./config.json"), @"[\{\}""]", "");

        await _bot.Api.Messages.SendAsync(new MessagesSendParams {
            RandomId = Rand.Next(),
            PeerId = message.PeerId,
            ReplyTo = message.Id,
            Message = help
        });
    }

    private async Task HandleSearchCommand(Message message, string alias, string args) {
        var (image, foundLocation) = await ServiceEndpoint.Map.Search(args);

        if (image == null) {
            _bot.Api.Messages.Send(new MessagesSendParams {
                RandomId = Rand.Next(),
                PeerId = message.PeerId,
                ReplyTo = message.Id,
                Message = $"Не удалось узнать где {args}!"
            });
            return;
        }

        var photo = await _bot.UploadImage(image);
        if (photo == null) return;

        var text = await MessageProcessor.KeepUpConversation();

        _bot.Api.Messages.Send(new MessagesSendParams {
            RandomId = Rand.Next(),
            PeerId = message.PeerId,
            ReplyTo = message.Id,
            Attachments = [photo],
            Message = $"{args} {text} \n{foundLocation.Item1}\n{foundLocation.Item2}",
            //Lat = long.Parse(output.Item2.lat),
            //Longitude = long.Parse(output.Item2.lon)
        });
    }

    private async Task HandlePythonCommand(Message message, string alias, string args) {
        var commandText = message.Text.Trim();
        var commandParts = commandText.Split([' '], 3);

        if (commandParts.Length < 3 || !commandParts[1].Equals("py", StringComparison.OrdinalIgnoreCase)) {
            Answer(message,
                "Пожалуйста, укажи Python-код после команды, например: `v py print('Hello')`");
            return;
        }

        var pythonCode = commandParts[2].Trim();
        L.I($"Received Python code: {pythonCode}");

        var pythonCodeLower = pythonCode.ToLower();
        if (Regex.IsMatch(pythonCodeLower, @"(os|sys|subprocess|import|exec|eval|\bimp\b|\bort\b)")) {
            Answer(message,
                "Использование системных модулей, импорта или опасных функций запрещено.");
            return;
        }

        if (pythonCode.Length > 1000) {
            Answer(message, "Слишком длинный код (максимум 1000 символов).");
            return;
        }

        try {
            var output = await ExecutePythonCode(pythonCode);
            Answer(message, output.Length > 0 ? output : "Код выполнен, но вывода нет.");
            L.I("Python code executed successfully.");
        } catch (Exception ex) {
            Answer(message, "Ошибка при выполнении кода: " + ex.Message);
            L.I($"Error executing Python code: {ex.Message}");
        }
    }

    private static async Task<Image<Rgba32>?> FindImageInMessage(Message message) {
        var attachments = message.Attachments;
        if (attachments is not { Count: > 0 } || attachments[0].Instance is not Photo photo) {
            if (message.ReplyMessage != null)
                return await FindImageInMessage(message.ReplyMessage);
            return null;
        }

        var largestPhoto = photo.Sizes.OrderByDescending(s => s.Width * s.Height).First();
        var photoUrl = largestPhoto.Url.AbsoluteUri;
        using var httpClient = new HttpClient();
        var response = await httpClient.GetAsync(photoUrl);
        var imageBytes = await response.Content.ReadAsByteArrayAsync();
        using var ms = new MemoryStream(imageBytes);
        Image<Rgba32> originalImage;

        try {
            originalImage = Image.Load<Rgba32>(ms);
        } catch (Exception e) {
            L.I($"Error loading image: {e.Message}");
            return null;
        }

        return originalImage;
    }

    public async Task HandleFuneralCommand(Message message, string alias, string args) {
        var sourceImage = await FindImageInMessage(message);
        if (sourceImage == null) {
            Answer(message, "Некого хоронить!");
            return;
        }

        var processedImage = await sourceImage.Funeral();

        var photo = await _bot.UploadImage(processedImage);
        if (photo == null) return;

        _bot.Api.Messages.Send(new MessagesSendParams {
            Message = "RIP🥀",
            RandomId = Rand.Next(),
            PeerId = message.PeerId,
            // ReplyTo = message.Id,
            Attachments = [photo]
        });

        L.I("Processed photo sent to user.");
    }

    private static async Task<string> ExecutePythonCode(string code) {
        var pythonPath = "python";

        var arguments = $"-c \"{code.Replace("\"", "\\\"")}\"";

        var startInfo = new ProcessStartInfo {
            FileName = pythonPath,
            Arguments = arguments,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        using var process = new Process { StartInfo = startInfo };
        var output = new StringBuilder();
        var error = new StringBuilder();

        process.OutputDataReceived += (sender, args) => {
            if (args.Data != null) output.AppendLine(args.Data);
        };
        process.ErrorDataReceived += (sender, args) => {
            if (args.Data != null) error.AppendLine(args.Data);
        };

        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        var completed = await Task.Run(() => process.WaitForExit(5000));

        if (!completed) {
            process.Kill();
            throw new Exception("Код выполнялся слишком долго (более 5 секунд).");
        }

        process.WaitForExit();

        if (error.Length > 0) {
            return $"Ошибка: {error.ToString().Trim()}";
        }

        return output.ToString().Trim();
    }

    private async Task HandleChaosCommand(Message message, string alias, string args) {
        var commandText = message.Text.Trim();
        var commandParts = commandText.Split([' '], 2); // "v chaos"

        if (commandParts.Length < 2 || !commandParts[1].Equals("chaos", StringComparison.OrdinalIgnoreCase)) {
            Answer(message, "Просто напиши: `v chaos`");
            return;
        }

        L.I("Starting chaos...");

        try {
            var members = await _bot.GetChatMembers(message.PeerId);
            var victim = members.OrderBy(x => Guid.NewGuid()).First();

            var randomMember2 = members.OrderBy(x => Guid.NewGuid()).First();

            var task = GenerateChaosTask(Bot.PingUser(randomMember2));

            var buttons = new List<MessageKeyboardButton> {
                new MessageKeyboardButton {
                    Action = new MessageKeyboardButtonAction {
                        Type = KeyboardButtonActionType.Text,
                        Label = "Выполнено",
                        Payload = JsonConvert.SerializeObject(new { command = "chaos_done", victim = victim.Id })
                    }
                },
                new MessageKeyboardButton {
                    Action = new MessageKeyboardButtonAction {
                        Type = KeyboardButtonActionType.Text,
                        Label = "Провал",
                        Payload = JsonConvert.SerializeObject(new { command = "chaos_fail", victim = victim.Id })
                    }
                }
            };

            var keyboard = new MessageKeyboard {
                Buttons = new List<List<MessageKeyboardButton>> { buttons },
                Inline = true
            };

            await _bot.Api.Messages.SendAsync(new MessagesSendParams {
                RandomId = Rand.Next(),
                PeerId = message.PeerId,
                Message = $"🔥 Хаос начинается! Жертва: {Bot.PingUser(victim)}\nЗадание: {task}\nГолосуйте!",
                Keyboard = keyboard
            });

            L.I($"Chaos task assigned to {victim.FirstName}: {task}");
        } catch (Exception ex) {
            Answer(message, "Ошибка хаоса: " + ex.Message);
            L.I($"Error in chaos: {ex.Message}");
        }
    }

    private static string GenerateChaosTask(string name) {
        string[] actions = [
            $"выебать {name}",
            $"трахнуть {name}",
            $"написать выебан на жопе {name}",
            $"пойти нахуй",
            $"сделать KYS",
            $"спонсировать побег в лес",
            $"выебать {name} в жопу с разбега",
            $"трахнуть {name} до потери пульса",
            $"засунуть {name} голову в унитаз и смыть",
            $"сломать {name} нос об стену",
            $"выбить {name} зубы кувалдой",
            $"раздавить {name} яйца прессом",
            $"заставить {name} спорить с зеркалом до слез",
            $"сказать {name}, что голоса в голове хотят пиццу",
            $"заставить {name} танцевать с воображаемой бабкой",
            $"написать на лбу {name} \"шиза внутри\" и отправить в магазин",
            $"убедить {name}, что его кот — агент ФСБ",
            $"заставить {name} шептать \"я нормальный\" в подушку всю ночь",
            $"подарить {name} пустую коробку как \"лекарство от голосов\"",
            $"сказать {name}, что его тень хочет его задушить",
            $"заставить {name} искать Wi-Fi в лесу от деревьев",
            $"убедить {name}, что он застрял в симуляции без выхода",
            $"заставить {name} петь колыбельную своему отражению",
            $"сказать {name}, что его мозг сбежал через уши",
            $"заставить {name} обнимать мусорку и называть ее мамой",
            $"убедить {name}, что дождь — это слезы его второго я",
            $"заставить {name} искать свою душу в унитазе",
            $"сказать {name}, что он умер, но не заметил",
            $"заставить {name} писать письма своему выдуманному другу в стену",
            $"убедить {name}, что он — картошка в прошлой жизни",
            $"заставить {name} кричать \"где мой разум\" в пустую комнату",
            $"сказать {name}, что его ноги — шпионы и следят за ним",
            $"заставить {name} рисовать круги и шептать \"это мой дом\"",
            $"убедить {name}, что лампа в комнате — его босс",
            $"заставить {name} носить носок на руке как вторую личность",
            $"сказать {name}, что его голоса в голове устраивают забастовку",
            $"заставить {name} искать таблетки в миске с макаронами",
            $"убедить {name}, что он видит мир в инверсии",
            $"заставить {name} гладить воздух и называть его псом"
        ];
        return actions[Rand.Next(actions.Length)];
    }

    #region Settings

    private async Task HandleSettingsCommand(Message message, string alias, string args) {
        var chat = _bot.Saves.Chats.FirstOrDefault(c => c.PeerId == message.PeerId);

        if (chat == null) {
            Answer(message, "Чат не найден.");
            return;
        }

        if (chat.Properties == null) {
            chat.Properties = new ChatProperties();
            _bot.Saves.Save();
        }

        var b1 = CreateToggleButton(chat.Properties.IsAnime, "anime", "Аниме");
        var b2 = CreateToggleButton(chat.Properties.IsHentai, "hentai", "Хентай");
        var b3 = CreateToggleButton(chat.Properties.IsImageProccestion, "image_processing", "Обработка изображений");
        var b4 = CreateToggleButton(chat.Properties.IsMeme, "meme", "Мемы");
        var b5 = CreateToggleButton(chat.Properties.IsWeather, "weather", "Погода");
        var b6 = CreateToggleButton(chat.Properties.IsLocation, "location", "Местоположение");

        var buttonsRow1 = new List<MessageKeyboardButton> { b1, b2, b3 };
        List<MessageKeyboardButton> buttonsRow2 = [b4, b5, b6];

        var values = new List<List<MessageKeyboardButton>> { buttonsRow1, buttonsRow2 };

        var keyboard = new MessageKeyboard {
            Buttons = values,
            Inline = true
        };

        await _bot.Api.Messages.SendAsync(new MessagesSendParams {
            RandomId = Rand.Next(),
            PeerId = message.PeerId,
            Message = "Настройки чата",
            Keyboard = keyboard
        });
    }

    // Метод для создания переключающих кнопок
    private MessageKeyboardButton CreateToggleButton(bool isEnabled, string command, string label) {
        return new MessageKeyboardButton {
            Action = new MessageKeyboardButtonAction {
                Type = KeyboardButtonActionType.Text,
                Label = isEnabled ? $"✅ {label}" : $"🚫 {label}",
                Payload = JsonConvert.SerializeObject(new { command = $"toggle_{command}" })
            }
        };
    }

    private async Task<bool> IsUserAdmin(long chatId, long userId) {
        try {
            var members = await _bot.Api.Messages.GetConversationMembersAsync(chatId, null, Auth.Instance.GroupId);

            var admins = members.Items.Where(x => x.IsAdmin).Select(x => x.MemberId);
            return admins.Contains(userId);
        } catch (ConversationAccessDeniedException ex) {
            L.I($"Access denied to chat {chatId}: {ex.Message}");
            return false;
        }
    }

    /// Payload - данные, переданные в сообщении при нажатии кнопки
    public async Task HandlePayload(Message message) {
        dynamic payload = JsonConvert.DeserializeObject(message.Payload);
        string command = payload.command;
        var peerId = message.PeerId;

        switch (command) {
            case "anim":
                string tags = payload._tags;
                HandleAnimeCommand(message, command, tags);
                return;
            case "hen":
                string tagshen = payload._tags;
                HandleHCommand(message, command, tagshen);
                return;

            case "chaos_done":
                long doneVictim = payload.victim;
                chaosScores[doneVictim] = chaosScores.GetValueOrDefault(doneVictim) + 1;
                Answer(message,
                    $"Задание выполнено! [id{doneVictim}|Жертва] получает +1 хаос-очко. Текущий счёт: {chaosScores[doneVictim]}");
                return;

            case "chaos_fail":
                long failVictim = payload.victim;
                chaosScores[failVictim] = chaosScores.GetValueOrDefault(failVictim) - 1;
                Answer(message,
                    $"Задание провалено! [id{failVictim}|Жертва] теряет 1 хаос-очко. Текущий счёт: {chaosScores[failVictim]}");
                return;
        }

        var userId = message.FromId.Value;

        if (!await IsUserAdmin(message.PeerId, userId)) {
            Answer(message, "Только админы могут изменять настройки.");
            return;
        }

        var chat = _bot.Saves.Chats.FirstOrDefault(c => c.PeerId == peerId);
        if (chat == null) {
            Answer(message, "Чат не найден.");
            return;
        }

        if (chat.Properties == null) {
            chat.Properties = new ChatProperties();
            _bot.Saves.Save();
        }

        switch (command) {
            case "toggle_anime":
                chat.Properties.IsAnime = !chat.Properties.IsAnime;
                break;
            case "toggle_hentai":
                chat.Properties.IsHentai = !chat.Properties.IsHentai;
                break;
            case "toggle_image_processing":
                chat.Properties.IsImageProccestion = !chat.Properties.IsImageProccestion;
                break;
            case "toggle_meme":
                chat.Properties.IsMeme = !chat.Properties.IsMeme;
                break;
            case "toggle_weather":
                chat.Properties.IsWeather = !chat.Properties.IsWeather;
                break;
            case "toggle_location":
                chat.Properties.IsLocation = !chat.Properties.IsLocation;
                break;

            default:
                Answer(message, "Неизвестная команда.");
                return;
        }

        _bot.Saves.Save();

        Answer(message, "Настройки обновлены.");

        // Отправляем обновленную клавиатуру
        HandleSettingsCommand(message, command, "");
    }

    #endregion

    private async Task HandleWhoCommand(Message message, string alias, string args) {
        string[] prefixes = [
            "Уверен, что",
            "Определенно",
            "Твоя мама мне сказала, что",
            "В Библии написано, что",
            "Ящитаю, это",
            "На твоей жопе написано, что",
            "Аллах сказал, что",
            "По РЕН-ТВ рассказали, что",
            "В девятом классе изучали, что",
            "Всем известно, что",
            "Все и так знают:"
        ];
        if (Rand.NextSingle() < 0.05) {
            Answer(message, "А я откуда знаю?");
            return;
        }

        if (Rand.NextSingle() < 0.05) {
            Answer(message, "Никто.");
            return;
        }

        var text = args ?? string.Empty;
        // Я НЕНАВИЖУ NLP Я НЕНАВИЖУ NLP Я НЕНАВИЖУ NLP Я НЕНАВИЖУ NLP
        text = Regex.Replace(text, @"\bты\b", "я", RegexOptions.IgnoreCase);
        text = Regex.Replace(text, @"\bтебя\b", "меня", RegexOptions.IgnoreCase);
        text = Regex.Replace(text, @"\bо тебе\b", "обо мне", RegexOptions.IgnoreCase);
        text = Regex.Replace(text, @"\bтебе\b", "мне", RegexOptions.IgnoreCase);
        text = Regex.Replace(text, @"\bтобой\b", "мной", RegexOptions.IgnoreCase);
        text = Regex.Replace(text, @"\bтвой\b", "мой", RegexOptions.IgnoreCase);
        text = Regex.Replace(text, @"\bтвоя\b", "моя", RegexOptions.IgnoreCase);
        text = Regex.Replace(text, @"\bтвои\b", "мои", RegexOptions.IgnoreCase);
        text = Regex.Replace(text, @"\bтвоим\b", "моим", RegexOptions.IgnoreCase);
        text = Regex.Replace(text, @"\bтвоими\b", "моими", RegexOptions.IgnoreCase);
        text = Regex.Replace(text, @"\bтвоих\b", "моих", RegexOptions.IgnoreCase);
        text = Regex.Replace(text, @"\bтвоего\b", "моего", RegexOptions.IgnoreCase);
        text = Regex.Replace(text, @"\bтвоей\b", "моей", RegexOptions.IgnoreCase);
        text = Regex.Replace(text, @"\bтвоё\b", "моё", RegexOptions.IgnoreCase);
        text = Regex.Replace(text, @"\bтвое\b", "мое", RegexOptions.IgnoreCase);
        text = Regex.Replace(text, @"\bтвою\b", "мою", RegexOptions.IgnoreCase);
        text = Regex.Replace(text, @"\bтвоем\b", "моем", RegexOptions.IgnoreCase);
        text = Regex.Replace(text, @"\bтвоём\b", "моём", RegexOptions.IgnoreCase);
        text = text.Replace('?', '.');

        var users = await _bot.GetChatMembers(message.PeerId);
        var answerUser = users[Rand.Next(users.Count)];
        var prefix = prefixes[Rand.Next(prefixes.Length)];
        var decl = alias switch {
            "кого" => Bot.Declension.Gen,
            "кому" => Bot.Declension.Dat,
            "кем" => Bot.Declension.Abl,
            "о ком" => Bot.Declension.Ins,
            _ => Bot.Declension.Nom
        };
        Answer(message, $"{prefix} {Bot.PingUser(answerUser, decl: decl)} {text}");
    }

    private static async Task<(string url, string? text)> GetWikiPage(string title) {
        using var client = new HttpClient();
        var url = $"https://ru.wikipedia.org/w/api.php?action=query&format=json&prop=info|extracts&inprop=url" +
                  $"&redirects=true&exintro=true&explaintext=true&titles={Uri.EscapeDataString(title)}";


        var response = await client.GetAsync(url);

        if (!response.IsSuccessStatusCode) {
            return ($"ru.wikipedia.org/w/index.php?title={Uri.EscapeDataString(title)}&action=edit", null);
        }

        var jsonString = await response.Content.ReadAsStringAsync();

        var data = JObject.Parse(jsonString);

        var pages = (JObject)data["query"]!["pages"]!;
        foreach (var page in pages.Properties()) {
            var text = page.Value["extract"];
            var canonicalurl = page.Value["canonicalurl"];

            if (text != null) {
                // сосальная сеть втентакле не соответствует RFC 3986
                var s = canonicalurl.Value<string>().Split("//")[1]
                    .Replace("(", "%28")
                    .Replace(")", "%29")
                    .Replace("_", "%5F");
                return ($"\ud83d\udcc4 Источник: {s}", text.ToString());
            }
        }

        return ($"ru.wikipedia.org/w/index.php?title={Uri.EscapeDataString(title)}&action=edit", null);
    }

    private async Task HandleWikiCommand(Message message, string alias, string args) {
        var (url, text) = await GetWikiPage(args);
        if (text == null) {
            Answer(message, $"Нет такой статьи, напиши сам: {url}");
            return;
        }

        Answer(message, $"{text.Trim()}\n\n{url}");
    }

    private async Task HandleWhatCommand(Message message, string alias, string args) {
        var (_, text) = await GetWikiPage(args);
        if (text == null) {
            Answer(message, "А я ебу что ли?");
            return;
        }

        Answer(message, text.Trim());
    }

    private async Task HandleUpdateChat(Message message, string alias, string args) {
        _bot.UpdateChat(message.PeerId);
        Answer(message, "Обновил список участников чата.");
    }
}