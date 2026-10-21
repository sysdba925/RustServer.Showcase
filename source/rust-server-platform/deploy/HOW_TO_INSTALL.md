# RustServer — установка и очистка (Ubuntu 24.04)

**Быстрый сценарий для Multipass VM `rust-clean`.** Команды подписаны по терминалам: Windows PowerShell или Ubuntu. Рабочую VM `rust-dev` **не трогать**.

## 1. Установить RustServer

**Windows PowerShell** — скопировать актуальный Installer и войти в VM:

```powershell
git -C "E:\Projects\rust\rust-server-platform" pull --ff-only
multipass transfer "E:\Projects\rust\rust-server-platform\deploy\install.sh" rust-clean:/home/ubuntu/install.sh
multipass shell rust-clean
```

**Ubuntu** — проверить доступ к `sudo` и запустить установку:

```bash
sudo -n true && echo "SUDO OK"
UPDATER_RELEASE_CHANNEL=Development bash ~/install.sh
```

Installer попросит недостающие данные в следующем порядке:

| Запрос | Что вставить |
| --- | --- |
| `GitHub bootstrap token:` | **Bootstrap PAT:** Contents Read только для `rust-server-platform` и `RustServer.Updater` |
| `GitHub updater token:` | **Updater PAT:** Contents Read для всех репозиториев сервисов, чьи GitHub Releases загружает Updater |
| `GitHub plugins token:` | **Plugins PAT:** Contents Read/Write только для `RustServer.PluginFiles` и `RustServer.ConfigFiles` |
| `Google Client ID:`, `Google Client Secret:` | OAuth-реквизиты Auth |
| `Admin username:`, `Admin password:` | Учётная запись первого администратора; пароль от 12 символов |

Секреты при вводе отображаются звёздочками. Токены создаются в GitHub заранее, вводятся **три разных** значения; не вставляй их в команды, логи и чат. При повторной установке существующие защищённые креденшелы не запрашиваются повторно. Дождись `Bootstrap completed successfully`; `SKIP` обычно означает, что существующий компонент сохранён.

## 2. Сразу после установки — разрешить HTTP для отладки

**Только для тестовой VM.** Это **отключение требования HTTPS**, а не отключение HTTP. Для VPS этот шаг **не выполнять**.

**Ubuntu** (сразу после успешного Installer, **до Rollout**):

```bash
sudo sed -i '/^Auth__AllowInsecureHttp=/d; $a Auth__AllowInsecureHttp=true' /etc/rustserver/auth.env
```

Auth ещё не установлен и не запущен: **перезапускать его сейчас не нужно**. Если меняешь эту настройку *после* установки Auth, применить её можно так:

```bash
sudo systemctl restart rustserver-auth
```

## 3. Проверить Updater и выполнить Rollout

**Ubuntu** — проверить результат Installer:

```bash
curl -fsS http://127.0.0.1:8080/health
sudo systemctl is-active rustserver-updater.service
sudo systemctl is-active rustserver-updater-executor.socket
```

Ожидается `{"status":"Healthy"}` и `active` для обоих systemd-юнитов. Если проверки не прошли, смотри диагностику ниже.

**Windows PowerShell** — выйди из Ubuntu командой `exit`, затем открой **встроенный UI Updater**:

```powershell
$vmIp = (multipass info rust-clean --format json | ConvertFrom-Json).info.'rust-clean'.ipv4[0]
Start-Process ("http://" + $vmIp + ":8080/emergency")
```

На странице `/emergency` нажми **Rollout** и **не закрывай вкладку** до завершения. Updater выполнит `Prepare → Install` последовательно для Auth, Rcon, Control, Versions, Plugins, Snapshots, **PluginCatalog** и Web, ожидая `Succeeded` после каждой операции. При первой установке PluginCatalog его проверенный release script автоматически создаёт systemd-конфигурацию, PostgreSQL, схему БД и media root — эти действия принадлежат Catalog, а не Bootstrap или Updater.

- Если часть приложений уже установлена, будет **Resume Rollout** — оно пропустит установленные.
- Когда установлены все, кнопка исчезнет. Остановка браузера прервёт последовательность, но уже отправленная операция продолжит выполняться на сервере.
- Если установка остановилась, исправь причину и нажми **Resume Rollout**. Rollout **не заменяет** подготовку systemd, конфигураций и зависимостей.

После установки Web открой Dashboard → Server и выполни **Install Rust → Install Oxide → Start**. Сам Installer и Rollout **не устанавливают игровой сервер**.

### PluginCatalog — автоматическая первоначальная установка

Не нужны отдельные кнопки или SSH-команды для создания PostgreSQL/БД/media.
`Rollout` запускает проверенные `Prepare → Install`. На первой установке
Updater Executor вызывает принадлежащий Catalog скрипт
`provisioning/provision.sh install` из защищённого подготовленного артефакта.
Скрипт последовательно выполняет host → database → media; затем Updater
запускает Catalog и проверяет `/health/ready`.

Повторное нажатие `Resume Rollout` после исправления ошибки безопасно:
существующие БД, схема, изображения и конфигурация сохраняются; конфликтующие
конфигурации не перезаписываются автоматически. `Update` и `Rollback`
не вызывают первоначальный установочный скрипт.

**Для VM с уже установленным Updater:** сначала обнови Updater до нового Development-релиза через его SelfUpdate и только затем повтори Platform Bootstrap, который добавит `InstallHook` в существующий root-owned реестр. Старый Updater не поддерживает новый ключ и не должен работать с новым registry. На чистой VM новый Bootstrap сразу установит актуальный Updater.

**Важно:** старый Catalog `v0.1.0-dev.1` не содержит скрипта provisioning.
Для этого сценария должны быть опубликованы новые Development-релизы
**Updater** и **PluginCatalog**, а затем обновлены Updater и Bootstrap
registry. На уже запущенной VM повторный Installer не обновляет Updater автоматически.

При ошибке Catalog проверь журнал привилегированного Updater Executor,
сообщение операции и конфигурацию `/etc/rustserver/plugin-catalog/plugin-catalog.env`.
**Не удаляй** БД, media или releases для повторной попытки.

## NetworkAccess — проверка после Rollout

Этот раздел выполняй только после установки релиза `RustServer.Control`,
содержащего `NetworkAccess`, и запуска Rust. Bootstrap ставит `nftables`
и другие системные зависимости, но не регистрирует Helper. Updater Executor
устанавливает Helper вместе с релизом Control, настраивает systemd и запускает
Control с зависимостью от Helper. При загрузке helper устанавливает
fail-closed firewall guard до старта Docker.

На VPS проверь boundary helper и режим из самой VM:

```bash
sudo systemctl is-active rustserver-control.service
sudo systemctl is-enabled rustserver-network-access-helper.service
sudo systemctl is-active rustserver-network-access-helper.service
sudo stat -c '%U:%G:%a %n' /run/rustserver-network-access
curl -fsS http://127.0.0.1:5112/api/server/network-access
sudo nft list chain ip filter DOCKER-USER
```

Ожидаются `active`, `root:rustserver-control:750` для каталога socket и один
running Compose-контейнер `rust-server`. Отсутствие `ip filter DOCKER-USER`
означает неподдерживаемую Docker firewall-конфигурацию: NetworkAccess вернёт
ошибку вместо неполного применения правил.

Проверяй UDP **с отдельной машины вне VM**, фактическим Rust/Steam-клиентом
или другим UDP-проверяющим инструментом, способным подтвердить ответ сервера:

1. При `publicEnabled: true` порты `28015/udp` и `28017/udp` отвечают.
2. Установи `publicEnabled: false`; оба публичных порта, включая уже
   установленные UDP-потоки, перестают отвечать.
3. Проверь, что `maintenancePort` отвечает тому же контейнеру на
   `28015/udp`. Измени только `maintenancePort` через API и убедись по
   `docker inspect`, что container ID не изменился, а Rust не был
   перезапущен.
4. Перезапусти `rustserver-control.service`, затем `docker.service`; после
   каждого действия повтори проверку maintenance-порта и публичного режима.
   Helper читает текущий container IPv4 при каждом применении, поэтому после
   пересоздания контейнера перенаправление должно указывать на новый адрес.
5. Оставь `publicEnabled: false`, перезагрузи VPS и повтори внешний тест.
   Сохранённый режим должен примениться автоматически; до успешного
   восстановления release-owned fail-closed guard не открывает публичные
   игровые порты.

Не считай `nmap -sU` единственным доказательством доступности: для UDP он
часто сообщает `open|filtered`. Сохрани результат реального запроса Rust или
Steam из внешней сети, а также `journalctl -u rustserver-control` и
`journalctl -u rustserver-network-access-helper` при диагностике.

## 4. Полностью удалить RustServer из тестовой VM

**Внимание:** `cleanup.sh` удаляет **все** Docker-контейнеры, образы, тома и build cache в VM (не только RustServer), а также конфигурации, ключи приложений, релизы, сохранения и данные RustServer. **Сама VM, Ubuntu и системные пакеты остаются.** Только для выделенной тестовой VM `rust-clean`.

**Windows PowerShell:**

```powershell
multipass transfer "E:\Projects\rust\rust-server-platform\deploy\cleanup.sh" rust-clean:/home/ubuntu/cleanup.sh
multipass shell rust-clean
```

**Ubuntu:**

```bash
sudo bash ~/cleanup.sh
```

Для повторной установки вернись к **разделу 1**. Саму VM пересоздавать не требуется.

---

## VPS / не тестовая VM

**Windows PowerShell:**

```powershell
scp "E:\Projects\rust\rust-server-platform\deploy\install.sh" ubuntu@SERVER_IP:/home/ubuntu/install.sh
ssh ubuntu@SERVER_IP
```

**Ubuntu:**

```bash
sudo -n true && echo "SUDO OK"
UPDATER_RELEASE_CHANNEL=Development bash ~/install.sh
```

Замени `SERVER_IP` и `ubuntu` на свой адрес и логин. `Development` используется здесь для доступных prereleases; для Production нужны опубликованные стабильные релизы и выбранный канал `Production`. На VPS **не** устанавливай `Auth__AllowInsecureHttp=true`. До настройки HTTPS и авторизованного доступа не публикуй порт Updater `8080` в Интернет: Updater API не имеет собственной прикладной аутентификации. `sudo -n true` должен проходить; не запускай сам Installer через `sudo bash`.

## Если что-то не заработало

**Ubuntu — диагностика:**

```bash
sudo systemctl status rustserver-updater.service --no-pager
sudo journalctl -u rustserver-updater.service -n 80 --no-pager
sudo systemctl status nginx.service --no-pager
curl -fsS http://127.0.0.1:8080/api/applications/registrations
```

| Симптом | Действие |
| --- | --- |
| `invalid option name: pipefail` | Файл с CRLF: `sed -i 's/\r$//' ~/install.sh` и повтори запуск |
| `sudo -n true` не проходит | Настрой passwordless sudo для установочного пользователя |
| Ошибка GitHub / скачивания | Проверь права соответствующего из трёх токенов и наличие релиза выбранного канала |
| `FAIL` при Bootstrap | Установка не завершилась; исправь указанную причину и повтори `UPDATER_RELEASE_CHANNEL=Development bash ~/install.sh` |
| `SKIP`, `Active Updater release is preserved` | Не ошибка: ранее установленное состояние сохраняется |
| `/health` не отвечает | Проверь systemd-журнал выше и порт в `/etc/rustserver/updater.env` |
| До Rollout `/login` / `/dashboard` отвечает 404/502 | Auth/Web ещё не установлены; это ожидаемо |
| Rollout остановился | Смотри статус/ошибку операции в Updater, устрани причину, затем `Resume Rollout` |

**Повторный запуск Installer** сохраняет существующие релизы, настройки и секреты; он не обновляет автоматически уже установленный Updater.

## Справка — постоянные файлы

| Назначение | Путь |
| --- | --- |
| Конфигурация Updater | `/etc/rustserver/updater.env` |
| Канал релизов | `/etc/rustserver/updater-release-channel` |
| Реестр приложений | `/etc/rustserver/updater/applications.ini` |
| Updater PAT | `/etc/rustserver/updater-github-token` |
| Plugins PAT | `/etc/rustserver/plugins-github-token` |
| Авторизация и Google OAuth | `/etc/rustserver/auth.env` |
| Активный Updater | `/opt/rustserver/updater/current` |
| База Auth | `/var/lib/rustserver/auth/auth.db` |

Подробности устройства Bootstrap, политики токенов и конфигураций: [Production Bootstrap README](bootstrap/README.md), [Runtime Configuration Layout](../docs/architecture/runtime-configuration-layout.md).
