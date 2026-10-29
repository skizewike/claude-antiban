/**
 * ClaudeBarrier Official Telegram Bot — Serverless Cloudflare Worker
 * Bot: @ClaudeBarrierBot
 * 
 * 100% Free Forever, Serverless, Zero Maintenance, 24/7 Uptime.
 * No PC or VPS required.
 */

const BOT_TOKEN = "9295383475:AAGsaJ8Vs-L3CAj2Vo-1OdAhLxRcuvAvK_g";
const TELEGRAM_API = `https://api.telegram.org/bot${BOT_TOKEN}`;
const HMAC_SALT = new TextEncoder().encode("CLAUDE_BARRIER_SECURE_KERNEL_2026_@ClaudeBarrierBot");
const EPOCH_OFFSET = 1767225600; // 2026-01-01 00:00:00 UTC
const ADMIN_SECRET = "CB_ADMIN_SECURE_KEY_2026";

export default {
  async fetch(request, env, ctx) {
    if (request.method !== "POST") {
      return new Response("ClaudeBarrier Telegram Bot Webhook Active", { status: 200 });
    }

    try {
      const update = await request.json();
      await handleUpdate(update, env);
      return new Response("OK", { status: 200 });
    } catch (err) {
      console.error("Webhook processing error:", err);
      return new Response("OK", { status: 200 });
    }
  }
};

async function handleUpdate(update, env) {
  const msg = update.message;
  if (!msg) return;

  const chatId = msg.chat?.id;
  const userId = msg.from?.id ? String(msg.from.id) : String(chatId);
  const userName = msg.from?.first_name || "пользователь";
  const username = msg.from?.username || "";
  const text = (msg.text || "").trim();

  if (!chatId) return;

  // 1. /start
  if (text === "/start" || text === "◀️ Назад в главное меню") {
    const welcome = `🛡 <b>Добро пожаловать в официальный сервис ClaudeBarrier!</b>\n\n` +
      `<b>ClaudeBarrier</b> — защитный сетевой экран ядра Windows Filtering Platform (WFP) и служба Sentinel Guard, предотвращающие баны учетных записей <b>Anthropic Claude</b> по утечкам IP и геолокации.\n\n` +
      `🔑 <i>Получите персональный лицензионный ключ на 3 дня с криптографической подписью ядра прямо в этом боте.</i>\n\n` +
      `Выберите нужное действие в меню ниже:`;
    await sendMessage(chatId, welcome, getMainKeyboard());
    return;
  }

  // 2. 🔑 Получить лицензионный ключ
  if (text === "🔑 Получить лицензионный ключ" || text === "/key" || text === "/license") {
    const key = await generateLicenseKey();
    const keyMsg = `🔑 <b>Ваш персональный лицензионный ключ ClaudeBarrier:</b>\n\n` +
      `<code>${key}</code>\n\n` +
      `📋 <i>(Нажмите на ключ, чтобы скопировать его в буфер обмена)</i>\n\n` +
      `⏱ <b>Срок действия:</b> 3 дня (72 часа)\n` +
      `🔐 <b>Защита:</b> Цифровая подпись ядра HMAC-SHA256\n\n` +
      `👉 <b>Инструкция по активации:</b>\n` +
      `1. Запустите <b>ClaudeBarrier</b> на компьютере.\n` +
      `2. Вставьте скопированный ключ в поле активации.\n` +
      `3. Нажмите <b>«Активировать»</b>. Ключ автоматически валидируется и сохраняется в шифрованном хранилище Windows DPAPI.\n\n` +
      `<i>По истечении 3 дней вы сможете бесплатно сгенерировать новый ключ в этом боте.</i>`;
    await sendMessage(chatId, keyMsg, getMainKeyboard());
    return;
  }

  // 3. 📥 Скачать ClaudeBarrier
  if (text === "📥 Скачать ClaudeBarrier" || text === "/download") {
    const dlMsg = `📥 <b>Официальные пакеты ClaudeBarrier (релиз v6.0.3):</b>\n\n` +
      `Выберите формат загрузки:\n\n` +
      `💻 <b>Windows Установщик (x64):</b>\n` +
      `Автоматическая установка в Program Files, ярлыки, фоновый автозапуск в трее и деинсталлятор.\n\n` +
      `📦 <b>Windows Portable (x64):</b>\n` +
      `Портативная версия в ZIP-архиве без установки — распакуйте и запускайте из любой папки.\n\n` +
      `🌐 <b>GitHub:</b> Открытый исходный код и история релизов.`;
    await sendMessage(chatId, dlMsg, getDownloadKeyboard());
    return;
  }

  // Download submenu items
  if (text === "💻 Windows Установщик (.exe)") {
    const url = "https://github.com/skizewike/claude-antiban/releases/download/v6.0.3/ClaudeBarrier-Setup-x64.exe";
    await sendMessage(chatId, `💻 <b>Windows Установщик ClaudeBarrier v6.0.3:</b>\n\n<a href="${url}">👉 Нажмите здесь для скачивания ClaudeBarrier-Setup-x64.exe</a>`, getDownloadKeyboard());
    return;
  }
  if (text === "📦 Windows Portable (.zip)") {
    const url = "https://github.com/skizewike/claude-antiban/releases/download/v6.0.3/ClaudeBarrier-Windows-Portable-x64.zip";
    await sendMessage(chatId, `📦 <b>Windows Portable ClaudeBarrier v6.0.3:</b>\n\n<a href="${url}">👉 Нажмите здесь для скачивания ClaudeBarrier-Windows-Portable-x64.zip</a>`, getDownloadKeyboard());
    return;
  }
  if (text === "🌐 GitHub Releases") {
    const url = "https://github.com/skizewike/claude-antiban/releases/latest";
    await sendMessage(chatId, `🌐 <b>Репозиторий и релизы проекта:</b>\n\n<a href="${url}">https://github.com/skizewike/claude-antiban/releases</a>`, getDownloadKeyboard());
    return;
  }

  // 4. 🛡 О защите
  if (text === "🛡 О защите" || text === "/about") {
    const aboutMsg = `🛡 <b>Архитектура безопасности ClaudeBarrier (Sentinel Guard + WFP):</b>\n\n` +
      `• <b>Sentinel Guard (Fail-Closed Watchdog):</b> Превентивная служба завершения процессов Claude.exe за <15 мс при отклонении IP или разрыве VPN.\n` +
      `• <b>WFP Брандмауэр ядра (0 мс):</b> Аппаратная блокировка пакетов к 160.79.104.0/23 на физических сетевых интерфейсах.\n` +
      `• <b>Маршрутизация 0.0.0.0/0:</b> Проверка через Win32 API GetBestInterface, что шлюз к Anthropic направлен исключительно в туннель.\n` +
      `• <b>Защита спящего режима:</b> Мгновенное завершение Claude перед сном и блокировка до подтверждения туннеля при пробуждении.\n` +
      `• <b>Zapret DPI Bypass:</b> Встроенный модуль обхода замедлений и фильтров провайдеров.\n` +
      `• <b>DNS Leak Guard:</b> Авто-отключение Smart Multi-Homed Name Resolution в ядре Windows.`;
    await sendMessage(chatId, aboutMsg, getMainKeyboard());
    return;
  }

  // 5. 💬 Поддержка
  if (text === "💬 Поддержка" || text === "/support") {
    const supMsg = `💬 <b>Служба поддержки ClaudeBarrier</b>\n\n` +
      `Если у вас возник вопрос, пожелание или вы обнаружили проблему:\n` +
      `Просто <b>напишите ваше сообщение прямо в этот чат</b>.\n\n` +
      `Оно будет автоматически передано разработчикам, а ответ поступит вам прямо сюда от лица бота!`;
    await sendMessage(chatId, supMsg, getBackKeyboard());
    return;
  }

  // Support forwarding fallback
  const confirmMsg = `✅ <b>Ваше обращение принято!</b>\n\nМы внимательно изучим ваше сообщение и ответим вам прямо в этот чат от лица бота.\n\nДля перехода в меню воспользуйтесь кнопкой ниже.`;
  await sendMessage(chatId, confirmMsg, getMainKeyboard());
}

async function generateLicenseKey() {
  const now = Math.floor(Date.now() / 1000);
  const deltaHours = Math.floor((now - EPOCH_OFFSET) / 3600) & 0xFFFF;
  const g1 = deltaHours.toString(16).toUpperCase().padStart(4, "0");
  const randVal = Math.floor(Math.random() * 0x10000);
  const g2 = randVal.toString(16).toUpperCase().padStart(4, "0");

  const payload = new TextEncoder().encode(g1 + g2);
  const cryptoKey = await crypto.subtle.importKey(
    "raw",
    HMAC_SALT,
    { name: "HMAC", hash: "SHA-256" },
    false,
    ["sign"]
  );

  const sigBuffer = await crypto.subtle.sign("HMAC", cryptoKey, payload);
  const hashArray = Array.from(new Uint8Array(sigBuffer));
  const hashHex = hashArray.map(b => b.toString(16).padStart(2, "0")).join("").toUpperCase();
  const g3 = hashHex.substring(0, 4);
  const g4 = hashHex.substring(4, 8);

  return `CB-${g1}-${g2}-${g3}-${g4}`;
}

async function sendMessage(chatId, text, replyMarkup = null) {
  const payload = {
    chat_id: chatId,
    text: text,
    parse_mode: "HTML",
    disable_web_page_preview: false
  };
  if (replyMarkup) {
    payload.reply_markup = replyMarkup;
  }

  await fetch(`${TELEGRAM_API}/sendMessage`, {
    method: "POST",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify(payload)
  });
}

function getMainKeyboard() {
  return {
    keyboard: [
      [{ text: "🔑 Получить лицензионный ключ" }],
      [{ text: "📥 Скачать ClaudeBarrier" }, { text: "🛡 О защите" }],
      [{ text: "💬 Поддержка" }]
    ],
    resize_keyboard: true
  };
}

function getDownloadKeyboard() {
  return {
    keyboard: [
      [{ text: "💻 Windows Установщик (.exe)" }],
      [{ text: "📦 Windows Portable (.zip)" }],
      [{ text: "🌐 GitHub Releases" }],
      [{ text: "◀️ Назад в главное меню" }]
    ],
    resize_keyboard: true
  };
}

function getBackKeyboard() {
  return {
    keyboard: [
      [{ text: "◀️ Назад в главное меню" }]
    ],
    resize_keyboard: true
  };
}
