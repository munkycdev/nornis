# Tasks

- [x] 1. Steering docs updated for World/Campaign/Character vocabulary
- [x] 2. Mechanical rename Campaign→World across src/, tests/ (excluding applied migrations); solution builds
- [x] 3. Hand-written rename migration (tables, columns, indexes, constraint names); Down mirrors Up — rehearsed both directions on LocalDB with seeded data
- [x] 4. New entities: Campaign, CampaignStatus, Character, CampaignCharacter; Source.CampaignId
- [x] 5. EF configurations, repositories, migration for new tables + CharacterName backfill/drop (both directions)
- [x] 6. Application services: campaign CRUD, character CRUD + assignment, source campaign validation
- [x] 7. API controllers + contracts; sources accept/return campaignId; sources filter by campaign
- [x] 8. Extraction worker passes campaign context into prompt
- [x] 9. Blazor UI: rename, campaign management, character management, source campaign picker/filter
- [x] 10. Tests: renamed suites green + new coverage for services/authorization
- [x] 11. Full solution build + test run green; API smoke-tested end-to-end against LocalDB (world → campaign → characters → assignment → campaign-tagged sources → filters → clear/reassign)
