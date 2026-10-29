#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
ClaudeBarrier Official Telegram Bot
Bot: @ClaudeBarrierBot
Token: 9295383475:AAGsaJ8Vs-L3CAj2Vo-1OdAhLxRcuvAvK_g

Features:
- Cryptographic 3-day activation keys (CB-G1-G2-G3-G4)
- 1 key per user rate limiting (renews once every 3 days)
- Interactive menu with "Назад" (Back) navigation in all submenus
- Two-way customer support relay (admin replies directly from the bot's identity)
- Direct app downloads & security architecture documentation
"""

import urllib.request
import urllib.parse
import json
import time
import hmac
import hashlib
import random
import os
import sys
import re
import html

def html_escape(s):
    return html.escape(str(s))

BOT_TOKEN = os.environ.get("CLAUDEBARRIER_BOT_TOKEN", "9295383475:AAGsaJ8Vs-L3CAj2Vo-1OdAhLxRcuvAvK_g")
API_URL = f"https://api.telegram.org/bot{BOT_TOKEN}"
HMAC_SALT = b"CLAUDE_BARRIER_SECURE_KERNEL_2026_@ClaudeBarrierBot"
EPOCH_OFFSET = 1767225600  # 2026-01-01 00:00:00 UTC
KEY_VALIDITY_SECONDS = 72 * 3600  # 3 days = 72 hours
ADMIN_SETUP_SECRET = os.environ.get("CLAUDEBARRIER_ADMIN_SECRET", "CB_ADMIN_SECURE_KEY_2026")

DATA_DIR = os.path.join(os.path.dirname(os.path.abspath(__file__)), "data")
STORAGE_FILE = os.path.join(DATA_DIR, "bot_storage.json")

# In-memory states: user_id -> state ("MAIN", "SUPPORT")
user_states = {}
last_support_from_user = None

def load_storage():
    if not os.path.exists(DATA_DIR):
        try:
            os.makedirs(DATA_DIR, exist_ok=True)
        except Exception:
            pass
    if os.path.exists(STORAGE_FILE):
        try:
            with open(STORAGE_FILE, "r", encoding="utf-8") as f:
                return json.load(f)
        except Exception as e:
            print(f"[WARN] Error reading storage: {e}")
    return {"users": {}, "admin_ids": []}

def save_storage(data):
    try:
        if not os.path.exists(DATA_DIR):
            os.makedirs(DATA_DIR, exist_ok=True)
        with open(STORAGE_FILE, "w", encoding="utf-8") as f:
            json.dump(data, f, ensure_ascii=False, indent=2)
    except Exception as e:
        print(f"[ERR] Error saving storage: {e}")

storage = load_storage()

def generate_3day_key():
    now = time.time()
    delta_hours = int((now - EPOCH_OFFSET) / 3600) & 0xFFFF
    g1 = f"{delta_hours:04X}"
    g2 = f"{random.randint(0, 0xFFFF):04X}"
    payload = (g1 + g2).encode("utf-8")
    sig = hmac.new(HMAC_SALT, payload, hashlib.sha256).digest()[:4].hex().upper()
    g3 = sig[:4]
    g4 = sig[4:8]
    return f"CB-{g1}-{g2}-{g3}-{g4}"

def validate_key_signature(key):
    try:
        parts = key.strip().upper().split("-")
        if len(parts) != 5 or parts[0] != "CB":
            return False
        payload = (parts[1] + parts[2]).encode("utf-8")
        sig = hmac.new(HMAC_SALT, payload, hashlib.sha256).digest()[:4].hex().upper()
        return (parts[3] + parts[4]) == sig
    except Exception:
        return False

def tg_request(method, payload=None):
    url = f"{API_URL}/{method}"
    headers = {"User-Agent": "ClaudeBarrierBot/6.0.3"}
    data = None
    if payload is not None:
        data = json.dumps(payload).encode("utf-8")
        headers["Content-Type"] = "application/json"

    req = urllib.request.Request(url, data=data, headers=headers)
    try:
        with urllib.request.urlopen(req, timeout=30) as resp:
            return json.loads(resp.read().decode("utf-8"))
    except Exception as e:
        print(f"[ERR] API request {method} failed: {e}")
        return None

def send_message(chat_id, text, reply_markup=None):
    payload = {
        "chat_id": chat_id,
        "text": text,
        "parse_mode": "HTML",
        "disable_web_page_preview": False
    }
    if reply_markup is not None:
        payload["reply_markup"] = reply_markup
    return tg_request("sendMessage", payload)

# Keyboards
def get_main_keyboard():
    return {
        "keyboard": [
            [{"text": "🔑 Получить лицензионный ключ"}],
            [{"text": "📥 Скачать ClaudeBarrier"}, {"text": "🛡 О защите"}],
            [{"text": "💬 Поддержка"}]
        ],
        "resize_keyboard": True
    }

def get_download_keyboard():
    return {
        "keyboard": [
            [{"text": "💻 Windows Установщик (.exe)"}],
            [{"text": "📦 Windows Portable (.zip)"}],
            [{"text": "🌐 GitHub Releases"}],
            [{"text": "◀️ Назад в главное меню"}]
        ],
        "resize_keyboard": True
    }

def get_about_keyboard():
    return {
        "keyboard": [
            [{"text": "🔑 Получить лицензионный ключ"}],
            [{"text": "◀️ Назад в главное меню"}]
        ],
        "resize_keyboard": True
    }

def get_support_keyboard():
    return {
        "keyboard": [
            [{"text": "◀️ Назад в главное меню"}]
        ],
        "resize_keyboard": True
    }

def format_remaining_time(seconds):
    if seconds <= 0:
        return "истек"
    days = int(seconds // 86400)
    hours = int((seconds % 86400) // 3600)
    mins = int((seconds % 3600) // 60)
    parts = []
    if days > 0:
        parts.append(f"{days} дн.")
    if hours > 0:
        parts.append(f"{hours} ч.")
    if not parts and mins > 0:
        parts.append(f"{mins} мин.")
    return " ".join(parts) if parts else "< 1 мин."

def notify_admins(text):
    for admin_id in storage.get("admin_ids", []):
        try:
            send_message(admin_id, text)
        except Exception:
            pass

def handle_update(up):
    global last_support_from_user
    message = up.get("message")
    if not message:
        return

    chat = message.get("chat", {})
    chat_id = chat.get("id")
    from_user = message.get("from", {})
    user_id = str(from_user.get("id", chat_id))
    user_name = from_user.get("first_name", "пользователь")
    username = from_user.get("username", "")
    text = message.get("text", "").strip()

    if not chat_id:
        return

    # Check reply to support forward message from admin
    reply_to = message.get("reply_to_message")
    is_admin = int(user_id) in [int(a) for a in storage.get("admin_ids", [])]

    # Admin Registration command (secured with secret key)
    if text.startswith("/admin") or text.startswith("/setadmin"):
        parts = text.split()
        if len(parts) >= 2 and parts[1].strip() == ADMIN_SETUP_SECRET:
            if int(user_id) not in [int(a) for a in storage.get("admin_ids", [])]:
                storage.setdefault("admin_ids", []).append(int(user_id))
                save_storage(storage)
            send_message(chat_id, f"👑 <b>Вы успешно зарегистрированы как администратор ClaudeBarrier!</b>\n\nID: <code>{user_id}</code>\nВам будут приходить все обращения из раздела «Поддержка». Чтобы ответить пользователю, сделайте Reply на сообщение с заявкой или напишите:\n<code>/reply &lt;user_id&gt; &lt;текст ответа&gt;</code>", get_main_keyboard())
        else:
            send_message(chat_id, "🔒 <b>Доступ ограничен.</b> Для входа в панель администратора укажите секретный ключ:\n<code>/admin &lt;ключ_доступа&gt;</code>", get_main_keyboard())
        return

    # Admin command /reply <user_id> <text>
    if text.startswith("/reply") and is_admin:
        parts = text.split(maxsplit=2)
        if len(parts) >= 3:
            target_uid = parts[1].strip()
            reply_body = parts[2].strip()
            safe_reply = html_escape(reply_body)
            user_msg = (
                f"💬 <b>Ответ от службы поддержки ClaudeBarrier:</b>\n\n"
                f"{safe_reply}\n\n"
                f"<i>Если у вас остались вопросы, просто напишите их в этот чат.</i>"
            )
            res = send_message(int(target_uid), user_msg, get_main_keyboard())
            if res and res.get("ok"):
                send_message(chat_id, f"✅ <b>Ответ успешно доставлен пользователю {target_uid}!</b>")
            else:
                send_message(chat_id, f"❌ Не удалось отправить ответ пользователю {target_uid} (возможно, бот заблокирован).")
        else:
            send_message(chat_id, "ℹ️ Использование: <code>/reply &lt;user_id&gt; &lt;текст ответа&gt;</code>")
        return

    # Admin replying directly to forwarded message
    if is_admin and reply_to and text and not text.startswith("/"):
        orig_text = reply_to.get("text", "")
        # Extract user ID from original forward
        match = re.search(r"ID:\s*<code>(\d+)</code>", orig_text)
        target_uid = match.group(1) if match else str(last_support_from_user)
        if target_uid:
            safe_text = html_escape(text)
            user_msg = (
                f"💬 <b>Ответ от службы поддержки ClaudeBarrier:</b>\n\n"
                f"{safe_text}\n\n"
                f"<i>Если у вас остались вопросы, просто напишите их в этот чат.</i>"
            )
            res = send_message(int(target_uid), user_msg, get_main_keyboard())
            if res and res.get("ok"):
                send_message(chat_id, f"✅ <b>Ответ успешно отправлен пользователю {target_uid} от лица бота!</b>")
                return
            else:
                send_message(chat_id, f"❌ Ошибка отправки пользователю {target_uid}.")
                return

    # Back navigation button in any menu
    if text in ["◀️ Назад в главное меню", "Назад", "/menu"]:
        user_states[user_id] = "MAIN"
        msg = (
            f"🏠 <b>Главное меню ClaudeBarrier</b>\n\n"
            f"Выберите необходимое действие:"
        )
        send_message(chat_id, msg, get_main_keyboard())
        return

    # /start
    if text.startswith("/start"):
        user_states[user_id] = "MAIN"
        greeting = (
            f"🛡 <b>Добро пожаловать в ClaudeBarrier, {user_name}!</b>\n\n"
            f"<b>ClaudeBarrier</b> — автономный клиент сетевой безопасности и аппаратной фильтрации WFP, "
            f"разработанный для надежной защиты учетных записей <b>Anthropic Claude</b> (Claude Desktop, claude.ai и Claude Code CLI).\n\n"
            f"🔑 <b>Лицензирование:</b>\n"
            f"Для работы приложения требуется криптографический ключ. Ключ выдается <b>1 на человека</b> и обновляется <b>раз в 3 дня</b>.\n\n"
            f"Выберите пункт меню ниже:"
        )
        send_message(chat_id, greeting, get_main_keyboard())
        return

    # 🔑 Получить лицензионный ключ
    if text in ["🔑 Получить лицензионный ключ", "/key", "/getkey"]:
        user_states[user_id] = "MAIN"
        now = time.time()
        user_info = storage.get("users", {}).get(user_id)

        # Check if user already has an active, non-expired key
        if user_info and user_info.get("expires_at", 0) > now:
            existing_key = user_info["key"]
            rem_sec = user_info["expires_at"] - now
            rem_str = format_remaining_time(rem_sec)
            exp_date = time.strftime("%d.%m.%Y %H:%M UTC", time.gmtime(user_info["expires_at"]))

            reply = (
                f"🔑 <b>Ваш действующий лицензионный ключ:</b>\n\n"
                f"<code>{existing_key}</code>\n\n"
                f"⏳ <b>Статус:</b> Активен\n"
                f"🕒 <b>Осталось действия:</b> {rem_str} (до {exp_date})\n\n"
                f"ℹ️ <i>По правилам безопасности выдается <b>1 ключ на человека</b> на 3 дня. "
                f"Вы сможете получить новый ключ после окончания срока действия текущего ({exp_date}).</i>\n\n"
                f"<b>Инструкция по активации:</b>\n"
                f"1. Нажмите на ключ выше, чтобы скопировать его в буфер обмена.\n"
                f"2. Откройте приложение <b>ClaudeBarrier</b> на компьютере.\n"
                f"3. Вставьте ключ в поле активации и нажмите <b>«Активировать»</b>."
            )
            send_message(chat_id, reply, get_main_keyboard())
            return

        # Generate fresh 3-day key
        new_key = generate_3day_key()
        expires_at = now + KEY_VALIDITY_SECONDS
        exp_date = time.strftime("%d.%m.%Y %H:%M UTC", time.gmtime(expires_at))

        storage.setdefault("users", {})[user_id] = {
            "key": new_key,
            "issued_at": now,
            "expires_at": expires_at,
            "username": username,
            "first_name": user_name
        }
        save_storage(storage)

        reply = (
            f"✅ <b>Вам успешно выдан лицензионный ключ на 3 дня:</b>\n\n"
            f"<code>{new_key}</code>\n\n"
            f"⏳ <b>Срок действия:</b> 3 дня (до {exp_date})\n"
            f"🛡 <b>Защита:</b> HMAC-SHA256 подпись + привязка к Windows DPAPI\n\n"
            f"<b>Инструкция по активации:</b>\n"
            f"1. Нажмите на ключ, чтобы скопировать его.\n"
            f"2. Вставьте его в окно активации <b>ClaudeBarrier</b> на компьютере.\n"
            f"3. Нажмите кнопку <b>«Активировать»</b>.\n\n"
            f"ℹ️ <i>Ключ обновляется раз в 3 дня. Через 3 дня вы сможете получить здесь новый ключ.</i>"
        )
        send_message(chat_id, reply, get_main_keyboard())
        return

    # 📥 Меню скачивания
    if text in ["📥 Скачать ClaudeBarrier", "/download"]:
        user_states[user_id] = "DOWNLOAD"
        dl_text = (
            f"📥 <b>Официальные пакеты ClaudeBarrier (Релиз v6.0.3):</b>\n\n"
            f"Выберите нужную версию для загрузки:\n\n"
            f"• <b>Windows Установщик (x64)</b> — автоматическая инсталляция, интеграция в трей и автозапуск.\n"
            f"• <b>Windows Портативная (.zip)</b> — запуск без установки из любой папки.\n"
            f"🌐 Все релизы и контрольные суммы SHA-256 доступны на GitHub:\n"
            f"https://github.com/skizewike/claude-antiban/releases"
        )
        send_message(chat_id, dl_text, get_download_keyboard())
        return

    # Скачать: Windows Установщик
    if text == "💻 Windows Установщик (.exe)":
        url = "https://github.com/skizewike/claude-antiban/releases/download/latest/download/ClaudeBarrier-Setup-x64.exe"
        msg = (
            f"💻 <b>Официальный установщик Windows (x64):</b>\n\n"
            f"🔗 <a href=\"{url}\">Скачать ClaudeBarrier-Setup-x64.exe</a>\n\n"
            f"• Поддержка: Windows 10 / Windows 11 (x64)\n"
            f"• Требуются права администратора для применения фильтров WFP ядра."
        )
        send_message(chat_id, msg, get_download_keyboard())
        return

    # Скачать: Windows Portable
    if text == "📦 Windows Портативная (.zip)":
        url = "https://github.com/skizewike/claude-antiban/releases/download/latest/download/ClaudeBarrier-Windows-Portable-x64.zip"
        msg = (
            f"📦 <b>Портативная версия без установки:</b>\n\n"
            f"🔗 <a href=\"{url}\">Скачать ClaudeBarrier-Windows-Portable-x64.zip</a>\n\n"
            f"• Распакуйте в любую удобную папку и запустите <code>ClaudeBarrier.exe</code> от имени администратора."
        )
        send_message(chat_id, msg, get_download_keyboard())
        return

    # Скачать: GitHub Releases
    if text == "🌐 GitHub Releases":
        msg = (
            f"🌐 <b>Официальный репозиторий проекта на GitHub:</b>\n\n"
            f"🔗 https://github.com/skizewike/claude-antiban\n\n"
            f"Страница релизов: https://github.com/skizewike/claude-antiban/releases/latest"
        )
        send_message(chat_id, msg, get_download_keyboard())
        return

    # 🛡 О защите
    if text in ["🛡 О защите", "/about"]:
        user_states[user_id] = "ABOUT"
        about_text = (
            f"🛡 <b>Архитектура безопасности ClaudeBarrier:</b>\n\n"
            f"• <b>Аппаратная WFP-изоляция ядра:</b>\n"
            f"Фильтрация сетевых пакетов осуществляется через Windows Filtering Platform. "
            f"Прямой доступ к подсетям Claude (<code>160.79.104.0/23</code>) на физических сетевых интерфейсах аппаратно заблокирован.\n\n"
            f"• <b>Мгновенный KillSwitch:</b>\n"
            f"Постоянный мониторинг маршрутов и туннеля. При малейшем сбое VPN или утечке сокеты Claude немедленно изолируются за &lt;15 мс.\n\n"
            f"• <b>Zapret DPI Bypass:</b>\n"
            f"Интегрированный модуль обхода цензуры и DPI провайдеров для стабильного подключения.\n\n"
            f"• <b>DPAPI-шифрование лицензии:</b>\n"
            f"Ключи шифруются криптографическим модулем учетной записи Windows (Data Protection API) с SHA-256 HMAC подписью."
        )
        send_message(chat_id, about_text, get_about_keyboard())
        return

    # 💬 Меню поддержки
    if text in ["💬 Поддержка", "/support"]:
        user_states[user_id] = "SUPPORT"
        sup_text = (
            f"💬 <b>Служба поддержки и обратной связи ClaudeBarrier</b>\n\n"
            f"Вы можете задать любой вопрос по работе барьера, сообщить о проблеме или предложить улучшение.\n\n"
            f"✍️ <b>Просто напишите ваше сообщение в этот чат:</b>\n"
            f"Разработчик получит его и ответит вам прямо от лица этого бота!"
        )
        send_message(chat_id, sup_text, get_support_keyboard())
        return

    # Handle support messages / feedback from user to developer
    if user_states.get(user_id) == "SUPPORT" or not is_admin:
        last_support_from_user = user_id
        safe_name = html_escape(user_name)
        safe_username = html_escape(username) if username else 'без юзернейма'
        safe_text = html_escape(text)
        admin_alert = (
            f"📩 <b>Новое обращение в поддержку ClaudeBarrier!</b>\n\n"
            f"👤 <b>От:</b> {safe_name} (@{safe_username})\n"
            f"🆔 <b>ID:</b> <code>{user_id}</code>\n"
            f"📅 <b>Время:</b> {time.strftime('%d.%m.%Y %H:%M:%S UTC', time.gmtime())}\n\n"
            f"💬 <b>Сообщение:</b>\n"
            f"{safe_text}\n\n"
            f"👉 <i>Чтобы ответить пользователю от лица бота, сделайте Reply на это сообщение или введите:</i>\n"
            f"<code>/reply {user_id} &lt;текст ответа&gt;</code>"
        )
        notify_admins(admin_alert)

        confirm_text = (
            f"✅ <b>Ваше сообщение передано разработчику!</b>\n\n"
            f"Мы внимательно изучим обращение. Ответ поступит вам прямо в этот чат от лица бота.\n\n"
            f"Чтобы вернуться в главное меню, нажмите кнопку ниже."
        )
        send_message(chat_id, confirm_text, get_support_keyboard())
        return

    # Default fallback
    fallback = (
        f"🛡 Команда не распознана. Воспользуйтесь меню ниже для выбора действия."
    )
    send_message(chat_id, fallback, get_main_keyboard())

def run_polling():
    print("[+] ClaudeBarrierBot (@ClaudeBarrierBot) polling started...")
    print(f"[+] Loaded {len(storage.get('users', {}))} users and {len(storage.get('admin_ids', []))} admins from storage.")
    offset = 0
    while True:
        try:
            url = f"getUpdates?offset={offset}&timeout=25"
            resp = tg_request(url)
            if resp and resp.get("ok"):
                for up in resp.get("result", []):
                    offset = up["update_id"] + 1
                    handle_update(up)
            time.sleep(1)
        except KeyboardInterrupt:
            print("\n[*] Stopping bot...")
            break
        except Exception as e:
            print(f"[WARN] Polling exception: {e}")
            time.sleep(3)

if __name__ == "__main__":
    run_polling()
