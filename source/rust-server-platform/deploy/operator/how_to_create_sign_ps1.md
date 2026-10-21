Можно подписать `rust-clean.ps1` собственным сертификатом Code Signing. Для твоих локальных PowerShell-скриптов этого достаточно.

### 1. Создай сертификат

В Windows PowerShell:

```
$cert = New-SelfSignedCertificate `
    -Type CodeSigningCert `
    -Subject "CN=RustServer Local Scripts" `
    -CertStoreLocation "Cert:\CurrentUser\My"
```

### 2. Добавь сертификат в доверенные

```
$path = "$env:TEMP\RustServerCodeSigning.cer"

Export-Certificate -Cert $cert -FilePath $path | Out-Null

Import-Certificate -FilePath $path -CertStoreLocation "Cert:\CurrentUser\Root"

Import-Certificate -FilePath $path -CertStoreLocation "Cert:\CurrentUser\TrustedPublisher" | Out-Null
```

Это делает твой сертификат доверенным только для текущего пользователя Windows.

### 3. Подпиши скрипт

Находясь в `E:\Projects\rust\secrets`:

```
Set-AuthenticodeSignature `
    -FilePath ".\rust-clean.ps1" `
    -Certificate $cert `
    -HashAlgorithm SHA256
```

Проверка:

```
Get-AuthenticodeSignature .\rust-clean.ps1
```

Ожидаемый статус — `Valid`. После этого попробуй:

```
.\rust-clean.ps1
```

Важно: после любого изменения `.ps1` цифровая подпись становится недействительной. Нужно будет снова вызвать `Set-AuthenticodeSignature` с тем же сертификатом.

Сертификат с приватным ключом не экспортируй в GitHub и не передавай другим.

Есть вариант проще. Если у тебя политика `RemoteSigned`, файл может блокироваться лишь потому, что скачан из интернета. Тогда достаточно:

```
Unblock-File .\rust-clean.ps1
.\rust-clean.ps1
```

Это не цифровая подпись, а снятие метки скачанного файла. При политике `AllSigned` такой способ не поможет.

Проверить политику:

```
Get-ExecutionPolicy -List
```

### 4. подписать новую версию файла.
```
$cert = Get-ChildItem Cert:\CurrentUser\My -CodeSigningCert |
    Where-Object Subject -eq "CN=RustServer Local Scripts" |
    Select-Object -First 1

Set-AuthenticodeSignature -FilePath ".\rust-clean.ps1" -Certificate $cert -HashAlgorithm SHA256
```