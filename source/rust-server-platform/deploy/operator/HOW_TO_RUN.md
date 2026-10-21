# Локальный оператор RustServer

`rust-clean.ps1` работает с доступным по SSH Ubuntu-сервером, включая VM и VPS. Нужны Windows PowerShell 5.1 или PowerShell, стандартные `ssh` и `scp`, Python 3 и `sudo -n` на сервере. Администратор заранее настраивает SSH-доступ и проверяет ключ хоста. Скрипт не создаёт ключи и не изменяет `authorized_keys`.

Скопируйте `connection.env.example` в `connection.env` рядом со скриптом и укажите адрес, пользователя, SSH-порт, путь к приватному ключу и порты Updater и локального туннеля. `~/.ssh/...` означает профиль Windows; относительный путь разрешается относительно каталога скрипта. `UPDATER_PORT` должен соответствовать порту Updater на сервере. Скопируйте `secure.env.example` в `secure.env` и заполните семь значений: по одному `NAME=value` на строку, без кавычек и переносов строк. Оба файла исключены из Git. Защитите локальный файл credentials правами доступа.

После подписи запустите `deploy/operator/rust-clean.ps1` и выберите действие в меню. Можно передать `-Action Bootstrap`, `Clean`, `Tunnel` или `CloseTunnel`. Bootstrap передаёт скрипты через SCP и credentials через stdin SSH; credentials не сохраняются во временном файле на сервере. Установка использует `deploy/install.sh` и явный выбор канала Development или Production перед подключением. Updater привязывается к loopback. Настройка `Auth__AllowInsecureHttp=true` не применяется.

Clean вызывает `deploy/cleanup.sh` и **удаляет все Docker-контейнеры, образы, тома и build cache сервера**, а также данные RustServer. Очистка начинается после выбора пункта 2 или вызова `-Action Clean`, без дополнительного подтверждения. Проверьте адрес и резервные копии перед подтверждением.

Пункт 3 освобождает `TUNNEL_LOCAL_PORT`, открывает SSH-туннель к loopback Updater, проверяет `/health` и открывает `/emergency`. Пункт 4 освобождает тот же порт. Оба действия завершают любой процесс, слушающий указанный порт, включая запущенный вручную, и выводят его имя и PID. Закрытие PowerShell не закрывает туннель. При проблемах SSH смотрите локальный журнал в `%LOCALAPPDATA%\RustServer`.

После изменения файла подпишите его существующим сертификатом в текущем пользовательском хранилище (если сертификатов несколько, выберите нужный по отпечатку):

```powershell
$cert = Get-ChildItem Cert:\CurrentUser\My -CodeSigningCert | Where-Object { $_.Subject -eq 'CN=RustServer Local Scripts' -and $_.HasPrivateKey } | Select-Object -First 1
if (-not $cert) { throw 'Code-signing certificate with private key not found' }
Set-AuthenticodeSignature -FilePath .\deploy\operator\rust-clean.ps1 -Certificate $cert -HashAlgorithm SHA256
Get-AuthenticodeSignature .\deploy\operator\rust-clean.ps1
```

example of shh conn string
```powershell
ssh -i "C:\Users\TDA\.ssh\rust-server_key.pem" azureuser@20.79.248.72
```
