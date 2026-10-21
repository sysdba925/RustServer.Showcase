DO $catalog_validation$
BEGIN
    IF (
        SELECT count(*)
        FROM information_schema.tables
        WHERE table_schema = 'public'
          AND table_type = 'BASE TABLE'
          AND table_name IN ('plugins', 'plugin_sources', 'plugin_media', 'sites', 'tags', 'folders', 'plugin_tags', 'plugin_folders')
    ) <> 8 THEN
        RAISE EXCEPTION 'Catalog schema must contain all required plugin and organization tables.';
    END IF;

    IF EXISTS (
        SELECT 1
        FROM (VALUES
            ('plugins', 'id', 'uuid', NULL::integer, 'NO'),
            ('plugins', 'name', 'character varying', 200, 'NO'),
            ('plugins', 'slug', 'character varying', 160, 'NO'),
            ('plugins', 'description', 'character varying', 8000, 'YES'),
            ('plugins', 'author', 'character varying', 200, 'YES'),
            ('plugins', 'notes', 'text', NULL::integer, 'YES'),
            ('plugins', 'source_description', 'text', NULL::integer, 'YES'),
            ('plugins', 'price', 'numeric', NULL::integer, 'YES'),
            ('plugins', 'currency', 'character varying', 3, 'YES'),
            ('plugins', 'current_known_version', 'character varying', 100, 'YES'),
            ('plugins', 'last_published_date', 'date', NULL::integer, 'YES'),
            ('plugins', 'icon_media_id', 'uuid', NULL::integer, 'YES'),
            ('plugins', 'created_at', 'timestamp with time zone', NULL::integer, 'NO'),
            ('plugins', 'updated_at', 'timestamp with time zone', NULL::integer, 'NO'),
            ('plugin_sources', 'id', 'uuid', NULL::integer, 'NO'),
            ('plugin_sources', 'plugin_id', 'uuid', NULL::integer, 'NO'),
            ('plugin_sources', 'site_id', 'uuid', NULL::integer, 'YES'),
            ('sites', 'id', 'uuid', NULL::integer, 'NO'),
            ('sites', 'name', 'character varying', 200, 'NO'),
            ('sites', 'base_url', 'character varying', 2048, 'YES'),
            ('plugin_sources', 'url', 'character varying', 2048, 'NO'),
            ('plugin_sources', 'label', 'character varying', 200, 'YES'),
            ('tags', 'id', 'uuid', NULL::integer, 'NO'),
            ('tags', 'name', 'character varying', 200, 'NO'),
            ('tags', 'color', 'character varying', 7, 'NO'),
            ('folders', 'id', 'uuid', NULL::integer, 'NO'),
            ('folders', 'name', 'character varying', 200, 'NO'),
            ('plugin_tags', 'plugin_id', 'uuid', NULL::integer, 'NO'),
            ('plugin_tags', 'tag_id', 'uuid', NULL::integer, 'NO'),
            ('plugin_folders', 'plugin_id', 'uuid', NULL::integer, 'NO'),
            ('plugin_folders', 'folder_id', 'uuid', NULL::integer, 'NO'),
            ('plugin_media', 'id', 'uuid', NULL::integer, 'NO'),
            ('plugin_media', 'plugin_id', 'uuid', NULL::integer, 'NO'),
            ('plugin_media', 'kind', 'character varying', 32, 'NO'),
            ('plugin_media', 'caption', 'character varying', 500, 'YES'),
            ('plugin_media', 'sort_order', 'integer', NULL::integer, 'NO'),
            ('plugin_media', 'storage_key', 'character varying', 1024, 'YES'),
            ('plugin_media', 'external_url', 'character varying', 2048, 'YES'),
            ('plugin_media', 'content_type', 'character varying', 255, 'YES')
        ) AS expected(table_name, column_name, data_type, maximum_length, is_nullable)
        LEFT JOIN information_schema.columns actual
            ON actual.table_schema = 'public'
           AND actual.table_name = expected.table_name
           AND actual.column_name = expected.column_name
        WHERE actual.column_name IS NULL
           OR actual.data_type IS DISTINCT FROM expected.data_type
           OR actual.character_maximum_length IS DISTINCT FROM expected.maximum_length
           OR actual.is_nullable IS DISTINCT FROM expected.is_nullable
    ) THEN
        RAISE EXCEPTION 'Existing Catalog schema has missing or incompatible columns; no migration was applied.';
    END IF;

    IF EXISTS (SELECT 1 FROM information_schema.columns
        WHERE table_schema = 'public' AND table_name = 'plugin_sources' AND column_name = 'kind') THEN
        RAISE EXCEPTION 'Existing Catalog source kind column is incompatible; no migration was applied.';
    END IF;

    IF NOT EXISTS (SELECT 1 FROM information_schema.columns
        WHERE table_schema = 'public' AND table_name = 'plugins' AND column_name = 'price'
          AND numeric_precision = 18 AND numeric_scale = 2) THEN
        RAISE EXCEPTION 'Existing Catalog price column has incompatible precision; no migration was applied.';
    END IF;

    IF (
        SELECT count(*)
        FROM pg_constraint catalog_constraint
        JOIN pg_class relation ON relation.oid = catalog_constraint.conrelid
        JOIN pg_namespace schema ON schema.oid = relation.relnamespace
        WHERE schema.nspname = 'public'
          AND catalog_constraint.conname IN (
            'pk_plugins', 'pk_plugin_sources', 'pk_plugin_media', 'pk_sites', 'fk_plugin_sources_site',
            'ak_plugin_media_plugin_id_id', 'fk_plugin_sources_plugin',
            'fk_plugin_media_plugin', 'fk_plugins_icon_media',
            'ck_plugin_media_location', 'ck_plugin_media_sort_order',
            'pk_tags', 'pk_folders', 'pk_plugin_tags', 'pk_plugin_folders',
            'fk_plugin_tags_plugin', 'fk_plugin_tags_tag', 'fk_plugin_folders_plugin', 'fk_plugin_folders_folder'
        )
    ) <> 19 THEN
        RAISE EXCEPTION 'Existing Catalog schema is missing required constraints; no migration was applied.';
    END IF;

    IF (
        SELECT count(*)
        FROM pg_indexes
        WHERE schemaname = 'public'
          AND indexname IN (
              'ux_plugins_slug', 'ix_plugin_sources_site_id_plugin_id',
              'ix_plugin_media_plugin_id_sort_order', 'ix_plugin_tags_tag_id_plugin_id', 'ix_plugin_folders_folder_id_plugin_id'
          )
    ) <> 5 THEN
        RAISE EXCEPTION 'Existing Catalog schema is missing required indexes; no migration was applied.';
    END IF;
END
$catalog_validation$;
