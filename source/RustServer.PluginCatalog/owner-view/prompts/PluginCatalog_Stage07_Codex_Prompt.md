# Codex Prompt — RustServer.PluginCatalog / Stage 07 of 09
## Web Plugin Details, Sources & Media

**Репозиторий исполнения:** `E:\Projects\rust\RustServer.Web`  
**Базовый commit Stage 06:** `d79738bfeaa9717d1bbc9b6da3609fa8f47eaed2`  
**Связанный backend:** `E:\Projects\rust\RustServer.PluginCatalog` (Stage 05, commit `06d1619f3a1f2105e5de3acc620ff203eafec2b1`)  
**Changelog:** `E:\Projects\rust\rust-server-platform\docs\changelog`  
**Цель:** завершить пользовательские сценарии управления **записями каталога** через Web, не смешивая каталог с управлением файлами и деплоем Rust-плагинов.

## 0. Сначала изучить контекст и проверить границы

Прочитай полностью `RustServer.Web/AGENTS.md`, существующие:
- `src/features/plugins/catalog/CatalogPage.tsx`, `api.ts`, `CatalogPage.module.css`, тесты;
- `src/shared/api/plugin-catalog-client.ts`, `plugin-catalog-types.ts`, `request-json.ts`, `src/shared/config/environment.ts`;
- `src/app/router/router.tsx`, `src/features/plugins/components/PluginsSectionNavigation.tsx`;
- текущие UI-паттерны для подтверждений, ошибок, доступных кнопок, модальных окон и отображения media.

Из sibling repo `RustServer.PluginCatalog` прочитай **публичный** `docs/api-contract.md`, `docs/data-model.md`, `docs/architecture.md`, `README.md` и при доступности Development `GET /openapi/v1.json`. Backend API-контракт (OpenAPI для transport, semantic contract для смысла) — источник истины. Не придумывай маршруты, поля и операции по внутренним классам.

Прочитай релевантные UI-контракты `rust-server-platform/docs/ui` и правила shared changelog. Если UI-документы устарели или противоречат актуальному backend API, **зафиксируй gap**, не переписывай их вне текущего scope. Сохрани всё, что уже работает в Stage 06, и не изменяй другие репозитории, кроме **нового** shared changelog iteration.

**Известный gap:** в Development OpenAPI `ProblemDetails` не описано расширение `code`, хотя semantic contract/backend возвращают его. Не объявляй это поле типизированным публичным контрактом Web и не меняй backend в этой задаче; для корректных ошибок используй HTTP status, `title` и `detail`. В отчёте явно оставь задачу на исправление backend OpenAPI.

## 1. Граница Stage 07

Реализуй Web UX:
1. Детальная страница одного Plugin с полным описанием, известной версией, датами, иконкой и переходом назад к каталогу.
2. Просмотр и ручное CRUD-управление `PluginSource`.
3. Медиа-галерея, загрузка изображений/GIF, добавление внешних ссылок Video/Preview, правка caption/order, удаление.
4. Выбор/снятие главной иконки; использование её в списке Stage 06.
5. Удаление записи Plugin с подтверждением, только в рамках опубликованной семантики: `409 plugin_has_media` до удаления медиа.

**Не делать:** состояние установленных плагинов; Rust runtime; version scraping/update; Git/Release/Deploy; backend changes; authn/authz; S3/MS SQL; DataMigration; изменения `RustServer.Platform` Bootstrap, topology, systemd, Nginx, Vite development proxy или существующих backend контрактов. Stage 08 отвечает за реальные маршруты через Platform.

## 2. URL и навигация

- Сохранить `/dashboard/plugins/catalog`.
- Добавить понятный устойчивый route `"/dashboard/plugins/catalog/:pluginId"` для карточки Plugin (учитывая React Router `basename="/dashboard"`).
- Из списка Stage 06 переходить в карточку по названию/отдельному view action; существующие create/edit metadata не сломать.
- Вернуться назад к каталогу без смены навигации `Plugins Management / Catalog`; не создавать новый top-level sidebar item и не заменять существующую `PluginsPage`.
- Данные конкретного Plugin загружать через `GET /api/plugins/{pluginId}`. Показывать loading, missing (`404`), unavailable/retry и нормальную пустую галерею.
- Известная каталогу версия не является установленной версией: никогда не показывать Installed/Update available без отдельного контракта.

## 3. API-клиент и типы

Расширить независимый `src/shared/api/plugin-catalog-client.ts`, `plugin-catalog-types.ts`, `src/features/plugins/catalog/api.ts` **строго согласно Stage 05 контракту**:

Источники:
- `GET /api/plugins/{pluginId}/sources` при необходимости;
- `POST /api/plugins/{pluginId}/sources`;
- `PUT /api/plugins/{pluginId}/sources/{sourceId}`;
- `DELETE /api/plugins/{pluginId}/sources/{sourceId}`.

Медиа:
- `GET /api/plugins/{pluginId}/media` при необходимости;
- `POST /api/plugins/{pluginId}/media` (`multipart/form-data`: `file`, `kind`, optional `caption`, required `sortOrder`);
- `POST /api/plugins/{pluginId}/media/external` (JSON: `kind: Video|Preview`, `externalUrl`, optional `caption`, `sortOrder`);
- `PUT /api/plugins/{pluginId}/media/{mediaId}` — **только** `caption`, `sortOrder` (не менять bytes/kind);
- `DELETE /api/plugins/{pluginId}/media/{mediaId}`;
- `PUT /api/plugins/{pluginId}/icon` — `{mediaId: UUID|null}`;
- `DELETE /api/plugins/{pluginId}` — `204` только при отсутствии связанных Media.

Использовать существующие helpers `getJson`, `postJsonWithBody`, `putJson`, `deleteNoContent` где подходят. Для `FormData` через `fetch`/shared `requestJson` **не выставлять `Content-Type` вручную**. `204 No Content` не пытаться парсить как JSON. Не добавлять обходных URL, прямого localhost fallback или новых dev proxy.

Для локальных media `accessUrl` и `thumbnailAccessUrl` являются относительными `/api/...` путями: разрешать их через **`/plugin-catalog-api`**. Для external media `accessUrl` — **готовый абсолютный HTTP(S) URL**, его нельзя префиксовать `/plugin-catalog-api`, скачивать через Catalog API или преобразовывать в локальный путь. Внешние ссылки открывать безопасно (`rel="noopener noreferrer"`), не встраивать сторонний URL в iframe автоматически.

## 4. Карточка плагина и источники

У карточки читабельная структура (действительный дизайн опирается на существующие shared UI/styles):
- Header: имя, иконка/placeholder, `currentKnownVersion` или Unknown, `lastPublishedAt` или Unknown;
- полный `description`, slug, последние изменения записи каталога;
- секция **Sources**: видимый `kind`, label, URL; явные actions Add, Edit, Delete. Новые и изменённые URL принимают только HTTP(S), как в backend; enum строго из контракта.
- Source deletion с понятным подтверждением; на успехе перечитать актуальный Plugin, не оставлять stale UI.
- Прямое редактирование основных Plugin metadata можно переиспользовать из Stage 06 без размножения двух несовместимых форм. Не дописывать самообновление версий.

Общий подход: тонкая API-граница + локальный state feature; не грузить данные Catalog в `ControlPlaneProvider`, не делать общие хранилища, если их не требует реальный кейс.

## 5. Галерея и медиадействия

- Показать медиа в порядке `sortOrder`, затем `id` (использовать порядок backend, не вводить произвольную сортировку).
- Для локальных PNG/JPEG/WebP: в сетке thumbnail, при открытии original; для GIF thumbnail статичный первый кадр, original анимирован при просмотре.
- Нормальный fallback на пустую/недоступную картинку, понятные caption/kind, без автозагрузки полноразмерных оригиналов в списке.
- Upload UI: один файл PNG/JPEG/WebP/GIF, `kind` из допустимых local (`Icon`, `Screenshot`, `AnimatedImage`, `Preview`), caption, sortOrder; client-side hint на backend limits (10 MiB); backend остаётся источником проверки сигнатур и декодирования.
- Add external media: только `Video` или `Preview` + absolute HTTP(S) URL. Не пытаться загрузить его на сервер, preview iframe/видео-плеер не обязателен.
- Edit media: только caption и sortOrder, не изображать замену самого файла.
- Delete media: подтверждение; после успешного delete перечитать details/gallery.
- Выбор главной иконки — явное действие только у локальных media `kind=Icon`; кнопку Clear icon показывать при наличии выбранной иконки. API возвращает обновлённые details, список Catalog тоже должен получить актуальную иконку при следующей загрузке.
- Любая операция имеет local pending/error/success состояние; защищать от двойной отправки. Не делать глобальную панель операций для этих простых синхронных CRUD.

Удаление Plugin:
- отдельное действие с подтверждением, пояснить, что при наличии Media backend вернёт `409` и сначала нужно удалить медиа;
- успешное `DELETE` ведёт обратно к каталогу и обновляет список при новой загрузке;
- не выполнять каскадное удаление медиа в клиенте, не скрывать backend conflict.

## 6. Архитектура, UI и безопасность

- Структура остаётся `src/features/plugins/catalog/` с ясными функциональными поддеревьями/компонентами по мере необходимости; избегать огромного одного `CatalogPage.tsx` или `PluginDetailsPage.tsx`, но и не дробить каждую кнопку в отдельный сервис.
- Типы REST DTO — в `src/shared/api`, Web view state — в feature.
- Не вводить новую тяжёлую UI-библиотеку/видеоплеер ради одного экрана.
- Не смешивать media library с `RustServer.Plugins` runtime files и release workflows.
- Стараться использовать доступные компоненты и CSS-конвенции Web.
- Upload/download API до authn/authz **не production-secure**: не объявлять их готовыми к публичному доступу и не делать обходов авторизации на фронте.
- Не решать отсутствие dev topology stage 08 обходными proxy, localStorage cache, hardcoded `localhost:5180` либо fake-success responses.

## 7. Проверки — минимально, без тестовой матрицы

Добавь **1–2 небольших happy-path UI/API-client теста** на ключевой сценарий: загрузка деталей и управление Source/Media или выбор иконки через мок `PluginCatalogApi`. Переиспользуй test helpers существующего приложения, не создавай новый тестовый фреймворк. Поведение multipart и prefix можно покрыть одним простым тестом при необходимости. Никаких десятков corner cases/fixtures/100% coverage.

Запусти:
- `npm run lint`
- `npm run typecheck`
- `npm run test`
- `npm run build`

Если `RustServer.Platform` ещё не дал `/plugin-catalog-api` proxy и реального PostgreSQL, не заявляй о пройденном end-to-end. Наличие mock UI tests не доказывает работу с backend на VPS.

## 8. Документы и changelog

- Обновить `RustServer.Web/README.md` разделами Stage 07 и его честными эксплуатационными ограничениями.
- Занести существенное изменение в **новый** `rust-server-platform/docs/changelog/iteration-NNN.md` по следующему свободному номеру и шаблону. Существующий `iteration-108.md` не переписывать. Другие документы Platform/Bootstrap/UI contracts в Stage 07 не менять.
- Не менять `RustServer.PluginCatalog` или `RustServer.Plugins` репозитории; отсутствующее поле OpenAPI `ProblemDetails.code` перечислить в handoff как backend gap.
- Для Web и shared changelog сделать **отдельные коммиты**; не коммитить unrelated/untracked файлы.
- При любом отклонении от публичного API — остановиться в соответствующей части работы, отчётливо указать конкретную несовместимость; не добавлять выдуманный contract.

## 9. Критерии приёмки / отчёт

Stage 07 завершён, если:
1. Карточка открывается по UUID URL, загружает details и отображает существующие sources/media/icon.
2. Источники можно создать, изменить, удалить.
3. Local images/GIF upload, gallery display, media metadata edit/delete, external Video/Preview links и set/clear Icon доступны через **реальный опубликованный Catalog API** без предположений о Rust runtime.
4. Plugin delete уважает backend `409 plugin_has_media`.
5. Существующий Stage 06 список, навигация и формы работают; API-префикс неизменен.
6. Lint/typecheck/tests/build проходят; ограничения Stage 08 записаны.

В финальном отчёте укажи: changed files, краткие сценарии UI, контрактные документы, фактически запущенные проверки, известные ограничения (включая `ProblemDetails.code`, отсутствие Platform proxy, реального PostgreSQL, authn/authz), Git SHA Web/changelog и статус обоих рабочих деревьев.

**Не начинай Stage 08.**
