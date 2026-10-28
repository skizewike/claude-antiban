FROM python:3.11-alpine

WORKDIR /app

ENV PYTHONUNBUFFERED=1

COPY bot/ ./bot/

CMD ["python", "-u", "bot/bot.py"]
