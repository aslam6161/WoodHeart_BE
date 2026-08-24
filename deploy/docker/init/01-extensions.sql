-- ---------------------------------------------------------------------------
-- Extensions the schema relies on. Run once, at database creation.
-- ---------------------------------------------------------------------------

-- Trigram matching for product search. This is what lets a customer who types
-- "dinning tabel" still find the dining table — worth a great deal on a
-- storefront where most traffic is mobile and typed in a hurry.
CREATE EXTENSION IF NOT EXISTS pg_trgm;

-- Accent-insensitive search, so "decor" matches "décor".
CREATE EXTENSION IF NOT EXISTS unaccent;

-- Used by the number-sequence generator for order and booking numbers.
CREATE EXTENSION IF NOT EXISTS pgcrypto;
