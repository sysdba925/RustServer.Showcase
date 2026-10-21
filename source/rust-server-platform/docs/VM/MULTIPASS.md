# Multipass — шпаргалка Windows PowerShell

## Два инстанса

| Инстанс | Назначение | Удалять? | IP |
|---|---|---|---|
| `rust-clean` | Чистая Ubuntu для проверки Installer/Bootstrap/Updater | **Да, тестовый** | `10.77.88.10` (постоянный) |
| `rust-dev` | Рабочий стенд RustServer со старым Bootstrap | **Нет, сохранить** | Динамический, см. `multipass list` |

## Быстрые команды

Команды выполняются в **обычном PowerShell**. Для создания `rust-clean` через скрипт нужен PowerShell **от имени администратора**.

| Действие | Rust Clean — `rust-clean` | Rust Server — `rust-dev` |
|---|---|---|
| Список машин и IP | `multipass list` | `multipass list` |
| Создать | `& "E:\Projects\rust\create-vm.ps1"` **(администратор)** | Не пересоздавать |
| Запустить | `multipass start rust-clean` | `multipass start rust-dev` |
| Остановить | `multipass stop rust-clean` | `multipass stop rust-dev` |
| Перезапустить | `multipass restart rust-clean` | `multipass restart rust-dev` |
| Подключиться к Ubuntu | `multipass shell rust-clean` | `multipass shell rust-dev` |
| Выйти из Ubuntu | `exit` | `exit` |
| Информация о VM | `multipass info rust-clean` | `multipass info rust-dev` |
| Выполнить команду | `multipass exec rust-clean -- <команда>` | `multipass exec rust-dev -- <команда>` |
| Удалить полностью | `multipass stop rust-clean` затем `multipass delete --purge rust-clean` | **Не удалять** |

`create-vm.ps1` создаёт `rust-clean` с Ubuntu 24.04, 2 CPU, 2 ГБ RAM, 10 ГБ диска и дополнительным статическим IP `10.77.88.10`. Скрипт сам настраивает сеть `RustServerLab`; отдельный YAML-файл не нужен. Запускать только когда `rust-clean` ещё не существует. Если файл скрипта хранится не в `E:\Projects\rust`, поправь путь в таблице.

После установки Updater можно открыть: `http://10.77.88.10:8080/health`.

**Важно:** команда `multipass purge` без имени удаляет **все** ранее помеченные к удалению машины. Не используем её для тестового вайпа.

---

## Подробные примеры для Rust Server (`rust-dev`)

## Посмотреть виртуальные машины

```powershell
multipass list
```

Показывает имя, состояние и IP машины.

---

## Зайти в Ubuntu

```powershell
multipass shell rust-dev
```

Выйти обратно в Windows:

```bash
exit
```

---

## Запустить VM

```powershell
multipass start rust-dev
```

---

## Остановить VM

```powershell
multipass stop rust-dev
```

---

## Перезапустить VM

```powershell
multipass restart rust-dev
```

---

## Информация о VM

```powershell
multipass info rust-dev
```

Здесь можно посмотреть IP, CPU, RAM, диск и состояние.

---

# Файлы

## Передать файл из Windows → Ubuntu

Пример:

```powershell
multipass transfer C:\Temp\test.txt rust-dev:/home/ubuntu/test.txt
```

Общий формат:

```powershell
multipass transfer <файл-в-Windows> rust-dev:<путь-в-Ubuntu>
```

Например:

```powershell
multipass transfer C:\Projects\app.zip rust-dev:/home/ubuntu/app.zip
```

---

## Забрать файл из Ubuntu → Windows

```powershell
multipass transfer rust-dev:/home/ubuntu/test.txt C:\Temp\test.txt
```

Общий формат:

```powershell
multipass transfer rust-dev:<файл-в-Ubuntu> <путь-в-Windows>
```

---

# Общая папка Windows ↔ Ubuntu

Если нужно постоянно работать с файлами проекта, удобнее не копировать их каждый раз, а подключить Windows-папку внутрь VM.

Например Windows-папка:

```text
C:\Projects\RustServer
```

Подключить:

```powershell
multipass mount C:\Projects\RustServer rust-dev:/home/ubuntu/RustServer
```

После этого внутри Ubuntu:

```bash
cd ~/RustServer
```

Ты будешь видеть файлы из Windows напрямую.

Отключить папку:

```powershell
multipass unmount rust-dev:/home/ubuntu/RustServer
```

---

# Выполнить одну команду без входа в Ubuntu

Например:

```powershell
multipass exec rust-dev -- ls -la
```

Или:

```powershell
multipass exec rust-dev -- docker ps
```

Это удобно для скриптов и автоматического деплоя.

---

# Самое важное

В 90% случаев нужны только эти команды:

```powershell
multipass list
multipass start rust-dev
multipass shell rust-dev
multipass stop rust-dev
```

Передать файл:

```powershell
multipass transfer C:\path\file.txt rust-dev:/home/ubuntu/file.txt
```

Для постоянной работы с проектом:

```powershell
multipass mount C:\path\project rust-dev:/home/ubuntu/project
```

---

# Не удалять Rust Server

`rust-dev` содержит рабочий стенд со старым Bootstrap. Не выполнять для него `multipass delete`, `multipass purge` или пересоздание.

Для очистки **только тестового** `rust-clean` используй команду из таблицы выше.
