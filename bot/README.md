# 🤖 ClaudeBarrier Official Telegram Bot // @ClaudeBarrierBot

Официальный бот для выдачи 3-дневных лицензионных ключей `CB-XXXX-XXXX-XXXX-XXXX` с криптографической подписью ядра HMAC-SHA256, прямых ссылок на загрузку и связи со службой поддержки.

---

## ☁️ Развертывание в облаке (Автономная работа 24/7 без включенного ПК)

### Вариант 1. Бессерверный Cloudflare Worker (100% Бесплатно, без сервера и без ПК)
1. Откройте бесплатную панель [workers.cloudflare.com](https://workers.cloudflare.com/).
2. Нажмите **Create Worker** (или «Создать приложение» $\rightarrow$ «Worker»).
3. Вставьте содержимое файла [`bot/cloudflare_worker.js`](./cloudflare_worker.js) и нажмите **Deploy**.
4. Скопируйте полученный адрес вашего воркера (например: `https://claudebarrier-bot.username.workers.dev`).
5. Установите вебхук в браузере (замените URL на ваш):
   ```text
   https://api.telegram.org/bot9295383475:AAGsaJ8Vs-L3CAj2Vo-1OdAhLxRcuvAvK_g/setWebhook?url=https://claudebarrier-bot.username.workers.dev
   ```
6. **Готово!** Бот работает на серверах Cloudflare по всему миру 24/7/365, отвечает за 20 мс и не требует работающего компьютера.

---

### Вариант 2. Хостинг в 1 клик через GitHub (Render.com)
1. Зайдите на [render.com](https://render.com/) (бесплатная регистрация).
2. Нажмите **New +** $\rightarrow$ **Background Worker**.
3. Подключите ваш репозиторий `skizewike/claude-antiban`.
4. В поле **Start Command** укажите:
   ```text
   python -u bot/bot.py
   ```
5. Нажмите **Create Background Worker**. Render автоматически клонирует проект и запустит опрос бота в облаке 24/7.

---

### Вариант 3. На любом Linux VPS (Ubuntu / Debian / CentOS)
Выполните одну команду в терминале вашего сервера:
```bash
curl -sSL https://raw.githubusercontent.com/skizewike/claude-antiban/main/bot/deploy_vps.sh | bash
```
Скрипт автоматически создаст и запустит фоновую службу `systemd`, которая будет работать непрерывно и перезапускаться при перезагрузках сервера.

---

### Вариант 4. Локальный запуск на ПК для тестирования
```cmd
bot\run_bot.bat
```
или
```cmd
python -u bot/bot.py
```
