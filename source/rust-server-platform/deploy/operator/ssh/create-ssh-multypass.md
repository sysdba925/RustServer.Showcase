Я предлагаю перестать усложнять `rust-clean.ps1` и сначала научиться подключаться к Multipass VM обычным SSH, точно так же, как ты будешь подключаться к VPS.

Ничего в существующих скриптах пока менять не будем.

## Что мы хотим получить

Сейчас ты подключаешься так:

```
multipass shell rust-clean
```

А хотим подключаться так:

```
ssh ubuntu@192.168.64.10
```

IP здесь пример. Для VPS команда будет абсолютно такой же, только с другим IP.

## Шаг 1. Создаём свой SSH-ключ на Windows

SSH-ключ — это своего рода электронный пропуск. Одна его часть остаётся на твоём Windows, другую мы добавим в Ubuntu.

Открой PowerShell:

```
New-Item -ItemType Directory -Force "$HOME\.ssh" | Out-Null
ssh-keygen -t ed25519 -f "$HOME\.ssh\rustserver_admin"
```

Программа предложит задать пароль для защиты ключа. Для production я рекомендую его задать.

Получишь два файла:

- `rustserver_admin` — секретный ключ, остаётся на Windows.
- `rustserver_admin.pub` — публичный ключ, который можно передавать серверу.

## Шаг 2. Разрешаем этому ключу вход в Multipass

В PowerShell:

```
multipass transfer "$HOME\.ssh\rustserver_admin.pub" rust-clean:/home/ubuntu/rustserver_admin.pub
```

Теперь добавляем публичный ключ в список разрешённых на Ubuntu:

```
multipass shell rust-clean
```

В открывшейся Ubuntu выполни:

```
mkdir -p ~/.ssh
chmod 700 ~/.ssh
cat ~/rustserver_admin.pub >> ~/.ssh/authorized_keys
chmod 600 ~/.ssh/authorized_keys
rm ~/rustserver_admin.pub
exit
```

Теперь Ubuntu разрешает подключаться с твоим ключом. Это стандартный механизм SSH.&#x20;

[image](https://www.google.com/s2/favicons?domain=https://canonical.com\&sz=32)

Multipass documentation



## Шаг 3. Подключаемся без Multipass Shell

Узнай IP виртуальной машины:

```
multipass info rust-clean
```

Там будет строка `IPv4`. Возьми этот адрес.

Далее:

```
ssh -i "$HOME\.ssh\rustserver_admin" ubuntu@IP_АДРЕС
```

Вместо `IP_АДРЕС` впиши адрес из Multipass.

При первом подключении SSH может попросить подтвердить новый сервер. После проверки адреса и отпечатка ключа сервера подтверди подключение.

Если попал в Ubuntu — всё, у тебя работает собственный SSH, совершенно независимый от Multipass Shell.

## Шаг 4. Открываем туннель

После того как обычный SSH заработал, закрываешь Ubuntu командой `exit` и запускаешь в Windows:

```
ssh -i "$HOME\.ssh\rustserver_admin" -N -L 127.0.0.1:18080:127.0.0.1:8080 ubuntu@IP_АДРЕС
```

Открываешь:

[http://localhost:18080/emergency](http://localhost:18080/emergency?utm_source=chatgpt.com)

Для закрытия туннеля — Ctrl+C в PowerShell.
