# Tasks

- [ ] 1. Steering docs updated for World/Campaign/Character vocabulary
- [ ] 2. Mechanical rename Campaign→World across src/, tests/ (excluding applied migrations); solution builds
- [ ] 3. Hand-written rename migration (tables, columns, indexes, constraint names); Down mirrors Up
- [ ] 4. New entities: Campaign, CampaignStatus, Character, CampaignCharacter; Source.CampaignId
- [ ] 5. EF configurations, repositories, migration for new tables + CharacterName backfill/drop
- [ ] 6. Application services: campaign CRUD, character CRUD + assignment, source campaign validation
- [ ] 7. API controllers + contracts; sources accept/return campaignId; sources filter by campaign
- [ ] 8. Extraction worker passes campaign context into prompt
- [ ] 9. Blazor UI: rename, campaign management, character management, source campaign picker/filter
- [ ] 10. Tests: renamed suites green + new coverage for services/authorization
- [ ] 11. Full solution build + test run green
