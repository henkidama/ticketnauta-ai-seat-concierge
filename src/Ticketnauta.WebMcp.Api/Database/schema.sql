CREATE SCHEMA IF NOT EXISTS webmcp_demo;

CREATE TABLE IF NOT EXISTS webmcp_demo.events (
    id text PRIMARY KEY,
    name text NOT NULL,
    venue text NOT NULL,
    city text NOT NULL,
    starts_at timestamptz NOT NULL,
    tagline text NOT NULL,
    description text NOT NULL,
    accent_color text NOT NULL,
    is_featured boolean NOT NULL DEFAULT false
);

CREATE TABLE IF NOT EXISTS webmcp_demo.zones (
    event_id text NOT NULL REFERENCES webmcp_demo.events(id) ON DELETE CASCADE,
    code text NOT NULL,
    name text NOT NULL,
    price numeric(12, 2) NOT NULL CHECK (price >= 0),
    color text NOT NULL,
    sort_order integer NOT NULL,
    PRIMARY KEY (event_id, code)
);

CREATE TABLE IF NOT EXISTS webmcp_demo.seats (
    id text PRIMARY KEY,
    event_id text NOT NULL,
    zone_code text NOT NULL,
    row_label text NOT NULL,
    seat_number integer NOT NULL,
    x integer NOT NULL,
    y integer NOT NULL,
    is_accessible boolean NOT NULL DEFAULT false,
    is_seed_blocked boolean NOT NULL DEFAULT false,
    is_sold boolean NOT NULL DEFAULT false,
    held_by_session text,
    hold_id uuid,
    hold_expires_at timestamptz,
    CONSTRAINT fk_seat_zone FOREIGN KEY (event_id, zone_code)
        REFERENCES webmcp_demo.zones(event_id, code) ON DELETE CASCADE,
    CONSTRAINT uq_seat_position UNIQUE (event_id, zone_code, row_label, seat_number)
);

CREATE TABLE IF NOT EXISTS webmcp_demo.holds (
    id uuid PRIMARY KEY,
    session_id text NOT NULL,
    event_id text NOT NULL REFERENCES webmcp_demo.events(id),
    status text NOT NULL CHECK (status IN ('active', 'released', 'expired', 'checked_out')),
    created_at timestamptz NOT NULL DEFAULT now(),
    expires_at timestamptz NOT NULL,
    released_at timestamptz,
    checked_out_at timestamptz
);

CREATE TABLE IF NOT EXISTS webmcp_demo.hold_items (
    hold_id uuid NOT NULL REFERENCES webmcp_demo.holds(id) ON DELETE CASCADE,
    seat_id text NOT NULL REFERENCES webmcp_demo.seats(id),
    PRIMARY KEY (hold_id, seat_id)
);

CREATE TABLE IF NOT EXISTS webmcp_demo.carts (
    session_id text PRIMARY KEY,
    event_id text NOT NULL REFERENCES webmcp_demo.events(id),
    hold_id uuid REFERENCES webmcp_demo.holds(id),
    updated_at timestamptz NOT NULL DEFAULT now()
);

CREATE TABLE IF NOT EXISTS webmcp_demo.cart_items (
    session_id text NOT NULL REFERENCES webmcp_demo.carts(session_id) ON DELETE CASCADE,
    seat_id text NOT NULL REFERENCES webmcp_demo.seats(id),
    PRIMARY KEY (session_id, seat_id)
);

CREATE TABLE IF NOT EXISTS webmcp_demo.checkouts (
    id uuid PRIMARY KEY,
    reference text NOT NULL UNIQUE,
    session_id text NOT NULL,
    event_id text NOT NULL REFERENCES webmcp_demo.events(id),
    total numeric(12, 2) NOT NULL,
    created_at timestamptz NOT NULL DEFAULT now()
);

CREATE TABLE IF NOT EXISTS webmcp_demo.schema_migrations (
    version integer PRIMARY KEY,
    applied_at timestamptz NOT NULL DEFAULT now()
);

-- Version 2 switches the public demo surface to English-only zone codes and seed data.
-- The one-time cleanup is restricted to this isolated schema.
DO $$
BEGIN
    IF NOT EXISTS (SELECT 1 FROM webmcp_demo.schema_migrations WHERE version = 2) THEN
        DELETE FROM webmcp_demo.checkouts;
        DELETE FROM webmcp_demo.cart_items;
        DELETE FROM webmcp_demo.carts;
        DELETE FROM webmcp_demo.hold_items;
        DELETE FROM webmcp_demo.holds;
        DELETE FROM webmcp_demo.seats;
        DELETE FROM webmcp_demo.zones;
        DELETE FROM webmcp_demo.events
        WHERE id IN ('costa-estelar-2026', 'noche-comedia-2027');
        INSERT INTO webmcp_demo.schema_migrations (version) VALUES (2);
    END IF;
END $$;

CREATE INDEX IF NOT EXISTS ix_seats_event_zone_row
    ON webmcp_demo.seats(event_id, zone_code, row_label, seat_number);
CREATE INDEX IF NOT EXISTS ix_seats_active_hold
    ON webmcp_demo.seats(event_id, hold_expires_at)
    WHERE hold_id IS NOT NULL;
CREATE INDEX IF NOT EXISTS ix_holds_session_event
    ON webmcp_demo.holds(session_id, event_id, created_at DESC);

INSERT INTO webmcp_demo.events
    (id, name, venue, city, starts_at, tagline, description, accent_color, is_featured)
VALUES
    (
        'neon-desert-2026',
        'Ticketnauta Live 2026: Neon Desert',
        'Horizon Demo Forum',
        'Tijuana, Mexico',
        '2026-11-21 20:30:00-08',
        'A fictional synth-pop night under the stars.',
        'A fully fictional event created to demonstrate intelligent seat search, selection, and temporary holds. It does not represent real inventory or a real sale.',
        '#9D7BFF',
        true
    ),
    (
        'stellar-coast-2026',
        'Stellar Coast Festival',
        'Tide Demo Amphitheater',
        'Ensenada, Mexico',
        '2026-12-05 17:00:00-08',
        'Three stages, one coastline, and zero real tickets.',
        'A fictional festival for comparing alternatives by budget and location. Every name, price, and seat is demo data.',
        '#27D7B3',
        false
    ),
    (
        'comedy-night-2027',
        'Comedy Night: Airplane Mode',
        'Prism Demo Theater',
        'Mexicali, Mexico',
        '2027-01-30 21:00:00-08',
        'Fictional stand-up for a very serious demo.',
        'A fictional show with a map and availability designed for the WebMCP challenge. Checkout is simulated and never processes payment.',
        '#FFB454',
        false
    )
ON CONFLICT (id) DO UPDATE SET
    name = EXCLUDED.name,
    venue = EXCLUDED.venue,
    city = EXCLUDED.city,
    starts_at = EXCLUDED.starts_at,
    tagline = EXCLUDED.tagline,
    description = EXCLUDED.description,
    accent_color = EXCLUDED.accent_color,
    is_featured = EXCLUDED.is_featured;

INSERT INTO webmcp_demo.zones (event_id, code, name, price, color, sort_order)
VALUES
    ('neon-desert-2026', 'diamond', 'Diamond', 2500.00, '#A78BFA', 1),
    ('neon-desert-2026', 'gold', 'Gold', 1900.00, '#F5C451', 2),
    ('neon-desert-2026', 'preferred', 'Preferred', 1400.00, '#42D6C4', 3),
    ('neon-desert-2026', 'general', 'General Admission', 900.00, '#6FB4FF', 4),
    ('stellar-coast-2026', 'diamond', 'Tide VIP', 2200.00, '#A78BFA', 1),
    ('stellar-coast-2026', 'gold', 'Breeze', 1650.00, '#F5C451', 2),
    ('stellar-coast-2026', 'preferred', 'Shore', 1200.00, '#42D6C4', 3),
    ('stellar-coast-2026', 'general', 'Lighthouse', 750.00, '#6FB4FF', 4),
    ('comedy-night-2027', 'diamond', 'Front Row', 1350.00, '#A78BFA', 1),
    ('comedy-night-2027', 'gold', 'Orchestra', 980.00, '#F5C451', 2),
    ('comedy-night-2027', 'preferred', 'Balcony', 720.00, '#42D6C4', 3),
    ('comedy-night-2027', 'general', 'Gallery', 490.00, '#6FB4FF', 4)
ON CONFLICT (event_id, code) DO UPDATE SET
    name = EXCLUDED.name,
    price = EXCLUDED.price,
    color = EXCLUDED.color,
    sort_order = EXCLUDED.sort_order;

WITH seat_rows(row_label, row_index) AS (
    VALUES ('A', 0), ('B', 1), ('C', 2), ('D', 3), ('E', 4)
), generated_seats AS (
    SELECT
        concat(z.event_id, ':', z.code, ':', r.row_label, ':', lpad(n::text, 2, '0')) AS id,
        z.event_id,
        z.code AS zone_code,
        r.row_label,
        n AS seat_number,
        100 + ((n - 1) * 50) AS x,
        100 + ((z.sort_order - 1) * 205) + (r.row_index * 32) AS y,
        -- Edge positions are accessible spaces; E2 and E11 remain standard companion seats.
        r.row_label = 'E' AND n IN (1, 12) AS is_accessible,
        CASE
            WHEN z.event_id = 'neon-desert-2026' AND z.code = 'gold' AND r.row_label = 'C' AND n IN (1, 2, 7, 8) THEN true
            WHEN z.event_id = 'neon-desert-2026' AND z.code = 'preferred' AND r.row_label = 'B' AND n IN (3, 9) THEN true
            WHEN z.event_id = 'stellar-coast-2026' AND z.code = 'diamond' AND r.row_label = 'A' AND n IN (2, 3, 9) THEN true
            WHEN z.event_id = 'comedy-night-2027' AND z.code = 'general' AND r.row_label = 'D' AND n IN (4, 5, 6, 10) THEN true
            ELSE ((n + r.row_index * 7 + z.sort_order * 11 + length(z.event_id)) % 31 = 0)
        END AS is_seed_blocked
    FROM webmcp_demo.zones z
    CROSS JOIN seat_rows r
    CROSS JOIN generate_series(1, 12) AS n
)
INSERT INTO webmcp_demo.seats
    (id, event_id, zone_code, row_label, seat_number, x, y, is_accessible, is_seed_blocked)
SELECT id, event_id, zone_code, row_label, seat_number, x, y, is_accessible, is_seed_blocked
FROM generated_seats
ON CONFLICT (id) DO UPDATE SET
    x = EXCLUDED.x,
    y = EXCLUDED.y,
    is_accessible = EXCLUDED.is_accessible,
    is_seed_blocked = EXCLUDED.is_seed_blocked;
