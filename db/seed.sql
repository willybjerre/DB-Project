-- Seed data for weather_compare.
-- Identitetskolonner (AppId, LocationId) udelades, så Postgres tildeler dem automatisk.
-- Tabel- og kolonnenavne er PascalCase (oprettet af EF Core), så de skal i dobbelte anførselstegn.

INSERT INTO "Applications" ("Name") VALUES
    ('Yr'),
    ('DMI');

INSERT INTO "Locations" ("Name", "Latitude", "Longitude", "ObservationStationId") VALUES
    ('Copenhagen', 55.6761, 12.5683, '06180');
