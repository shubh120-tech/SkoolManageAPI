-- Add address_line and date_of_joining to staff table.

ALTER TABLE staff
  ADD COLUMN IF NOT EXISTS address_line TEXT NULL,
  ADD COLUMN IF NOT EXISTS date_of_joining DATE NULL;
