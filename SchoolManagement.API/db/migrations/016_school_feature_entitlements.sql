-- Per-school feature toggles (e.g. WhatsApp Basic vs Premium). SuperAdmin manages via API.

CREATE TABLE IF NOT EXISTS school_feature_entitlements (
    school_id UUID NOT NULL REFERENCES schools(id) ON DELETE CASCADE,
    feature_key VARCHAR(80) NOT NULL,
    is_enabled BOOLEAN NOT NULL DEFAULT TRUE,
    updated_at TIMESTAMP NOT NULL DEFAULT NOW(),
    updated_by UUID NOT NULL,
    PRIMARY KEY (school_id, feature_key)
);

CREATE INDEX IF NOT EXISTS ix_school_feature_entitlements_school
ON school_feature_entitlements(school_id);
