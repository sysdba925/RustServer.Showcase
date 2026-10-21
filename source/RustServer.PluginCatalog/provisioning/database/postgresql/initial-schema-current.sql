BEGIN;

CREATE TABLE plugins (
    id uuid NOT NULL,
    name character varying(200) NOT NULL,
    slug character varying(160) NOT NULL,
    description character varying(8000),
    author character varying(200),
    notes text,
    source_description text,
    price numeric(18,2),
    currency character varying(3),
    current_known_version character varying(100),
    last_published_date date,
    icon_media_id uuid,
    created_at timestamp with time zone NOT NULL,
    updated_at timestamp with time zone NOT NULL,
    CONSTRAINT pk_plugins PRIMARY KEY (id)
);

CREATE TABLE sites (
    id uuid NOT NULL CONSTRAINT pk_sites PRIMARY KEY,
    name character varying(200) NOT NULL,
    base_url character varying(2048)
);

CREATE TABLE plugin_sources (
    id uuid NOT NULL,
    plugin_id uuid NOT NULL,
    site_id uuid,
    url character varying(2048) NOT NULL,
    label character varying(200),
    CONSTRAINT pk_plugin_sources PRIMARY KEY (id),
    CONSTRAINT fk_plugin_sources_plugin FOREIGN KEY (plugin_id)
        REFERENCES plugins (id) ON DELETE CASCADE,
    CONSTRAINT fk_plugin_sources_site FOREIGN KEY (site_id) REFERENCES sites (id) ON DELETE SET NULL
);

CREATE TABLE plugin_media (
    id uuid NOT NULL,
    plugin_id uuid NOT NULL,
    kind character varying(32) NOT NULL,
    caption character varying(500),
    sort_order integer NOT NULL,
    storage_key character varying(1024),
    external_url character varying(2048),
    content_type character varying(255),
    CONSTRAINT pk_plugin_media PRIMARY KEY (id),
    CONSTRAINT ak_plugin_media_plugin_id_id UNIQUE (plugin_id, id),
    CONSTRAINT ck_plugin_media_location CHECK (
        (storage_key IS NOT NULL AND btrim(storage_key) <> '' AND external_url IS NULL)
        OR (storage_key IS NULL AND external_url IS NOT NULL AND btrim(external_url) <> '')
    ),
    CONSTRAINT ck_plugin_media_sort_order CHECK (sort_order >= 0),
    CONSTRAINT fk_plugin_media_plugin FOREIGN KEY (plugin_id)
        REFERENCES plugins (id) ON DELETE CASCADE
);

ALTER TABLE plugins
    ADD CONSTRAINT fk_plugins_icon_media
    FOREIGN KEY (id, icon_media_id)
    REFERENCES plugin_media (plugin_id, id) ON DELETE RESTRICT;

CREATE UNIQUE INDEX ux_plugins_slug ON plugins (slug);
CREATE INDEX ix_plugin_sources_site_id_plugin_id ON plugin_sources (site_id, plugin_id);
CREATE INDEX ix_plugin_media_plugin_id_sort_order ON plugin_media (plugin_id, sort_order);

CREATE TABLE tags (
    id uuid NOT NULL CONSTRAINT pk_tags PRIMARY KEY,
    name character varying(200) NOT NULL,
    color character varying(7) NOT NULL
);
CREATE TABLE folders (
    id uuid NOT NULL CONSTRAINT pk_folders PRIMARY KEY,
    name character varying(200) NOT NULL
);
CREATE TABLE plugin_tags (
    plugin_id uuid NOT NULL,
    tag_id uuid NOT NULL,
    CONSTRAINT pk_plugin_tags PRIMARY KEY (plugin_id, tag_id),
    CONSTRAINT fk_plugin_tags_plugin FOREIGN KEY (plugin_id) REFERENCES plugins (id) ON DELETE CASCADE,
    CONSTRAINT fk_plugin_tags_tag FOREIGN KEY (tag_id) REFERENCES tags (id) ON DELETE CASCADE
);
CREATE INDEX ix_plugin_tags_tag_id_plugin_id ON plugin_tags (tag_id, plugin_id);
CREATE TABLE plugin_folders (
    plugin_id uuid NOT NULL,
    folder_id uuid NOT NULL,
    CONSTRAINT pk_plugin_folders PRIMARY KEY (plugin_id, folder_id),
    CONSTRAINT fk_plugin_folders_plugin FOREIGN KEY (plugin_id) REFERENCES plugins (id) ON DELETE CASCADE,
    CONSTRAINT fk_plugin_folders_folder FOREIGN KEY (folder_id) REFERENCES folders (id) ON DELETE CASCADE
);
CREATE INDEX ix_plugin_folders_folder_id_plugin_id ON plugin_folders (folder_id, plugin_id);

-- Initial schema for an empty Catalog database.

COMMIT;
