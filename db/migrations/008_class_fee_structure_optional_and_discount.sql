-- Optional fee heads (e.g. transport) and discount support.

-- 1. Class fee structure: mark which fee heads are optional (e.g. Transport). Mandatory by default.
ALTER TABLE class_fee_structures
ADD COLUMN IF NOT EXISTS is_optional BOOLEAN NOT NULL DEFAULT FALSE;

-- 2. Student fee assignments: optional discount_reason for audit (e.g. "Scholarship", "Sibling discount").
ALTER TABLE student_fee_assignments
ADD COLUMN IF NOT EXISTS discount_reason VARCHAR(200) NULL;
