# Requirements Document

## Introduction

Every AI call in Nornis runs on one Azure OpenAI resource that Dave pays for. Nornis is built
around AI: extraction, Ask the Loremaster, continuity, Prep. So a second user can't be let in
without either Dave paying for their table or the user paying for it themselves.

Feature 39 lets each user bring their own AI keys (OpenAI, Anthropic Claude, Google Gemini, or
any endpoint that speaks the OpenAI protocol) and choose which model does each kind of work.
A new user who has no key yet gets a small trial on Nornis's own Azure OpenAI resource,
on the **same models Nornis uses today**. A cheaper model would make the first impression
worse than the product: the Loremaster was moved off gpt-5.4-mini on 2026-07-16 for answer
quality, and extraction is where a smaller model loses the most.

Decided with Dave on 2026-11-03 (thread "Decide on bring-your-own AI keys", discussion doc
"Bring-your-own AI keys: options and recommendation").

## Glossary

- **AI key**: one user's credential for one AI provider, stored encrypted. A user has at most
  one per provider. Shown on Profile, never sent back to any browser after it is saved.
- **Provider**: OpenAI, Anthropic, Google Gemini, or an **OpenAI-compatible endpoint** (a base
  URL plus a key, such as Azure OpenAI, OpenRouter or a local Ollama).
- **AI task**: a kind of work that gets its own model choice: **Ask** (the Loremaster and the
  other answers a GM reads), **Extraction** (everything that proposes canon), **Prep**
  (drafting and the side conversation) and **Chores** (short background text such as world
  names and convergence notes).
- **Model catalog**: the list of models Nornis has tested for each task, with their prices and
  what they can do. A model enters the catalog only after it passes the extraction eval.
- **Key holder**: the user whose keys pay for a world's AI. It is always the world's creator,
  the same rule Bibliotaph uses for its connection.
- **Trial**: a one-time allowance in US dollars that a user's worlds may spend on Nornis's own
  key while that user has no key for the task.

## Requirements

### Requirement 1: A user keeps their own AI keys

**User Story:** As a GM, I want to give Nornis my own API key so my table's AI runs on my
account.

#### Acceptance Criteria

1. Profile SHALL show an **AI keys** card listing each saved key with its provider, its label,
   its last four characters, the date it was added, and whether its last check passed.
2. A user SHALL be able to add at most one key per provider. Saving a key for a provider that
   already has one SHALL replace it.
3. Before saving, Nornis SHALL check the key with one cheap call to the provider (listing
   models). A key that fails SHALL NOT be saved, and the card SHALL say what the provider
   answered (rejected, unreachable, no billing).
4. An OpenAI-compatible key SHALL also carry a base URL, a model name for each task the user
   wants it to serve, and the input and output price per million tokens for each of those
   models. A price of zero SHALL be allowed and SHALL mean free, as for a local model.
5. No API response, log line, telemetry event or Service Bus message SHALL contain a key.
   After saving, the key SHALL be shown only by its last four characters.
6. The card SHALL say that a ChatGPT, Claude or Gemini subscription is not an API key, and
   SHALL link to each provider's developer console.
7. A user SHALL be able to remove a key. Removing SHALL ask first, and SHALL name the tasks
   that will fall back to the trial or stop.

### Requirement 2: A user chooses the model for each task

**User Story:** As a GM with keys from more than one provider, I want to choose which model
extracts and which one answers.

#### Acceptance Criteria

1. For each AI task, Profile SHALL offer the catalog models whose provider the user has a key
   for, plus the models of the user's OpenAI-compatible key that are set for that task.
2. Each task SHALL have a default: the first catalog model, in catalog order, that the user's
   keys unlock. A user who has made no choice SHALL get the default.
3. A model that can't do what a task needs SHALL NOT be offered for it: a task that reads
   images SHALL only offer models that accept images, and Extraction SHALL only offer models
   whose structured output passed the eval.
4. OpenAI-compatible models SHALL be marked **not tested by Nornis**.

### Requirement 3: A world's AI runs on its creator's keys

**User Story:** As a player, I want to ask the Loremaster without having an AI account.

#### Acceptance Criteria

1. Every AI call that belongs to a world SHALL use the world creator's keys and model choices,
   whoever caused the call: the GM, a co-GM, a player, an anonymous public visitor, or a
   background job.
2. An AI call that belongs to no world yet (naming a new demo world) SHALL use the keys of
   the user who caused it.
3. Players and observers SHALL never be asked for a key.
4. A background job SHALL look up the key when it runs. A job message SHALL carry ids only.

### Requirement 4: A trial on Nornis's own key

**User Story:** As a new GM, I want to see what Nornis does before I go and get an API key.

#### Acceptance Criteria

1. Every user SHALL start with a trial allowance, $2.00 by default and set in configuration.
2. When the key holder has no key for the provider of the task's chosen model and has trial
   balance left, the call SHALL run on Nornis's own key, on the model Nornis uses for that
   task today.
3. The trial SHALL be one-time. It SHALL NOT renew each month.
4. All trial spend across all users SHALL stop for the rest of the calendar month once it
   reaches a configured ceiling ($25.00 by default). The global AI pause SHALL stop it as
   well.
5. While a world runs on the trial, public Ask SHALL be off for it. Anonymous visitors SHALL
   never spend Nornis's money.
6. Wherever a GM starts AI work, Nornis SHALL show that the trial is paying and how much is
   left ("Trial: $1.40 left"), with a link to add a key.
7. A call SHALL be allowed to finish once it has started, even if it takes the balance below
   zero. The next call SHALL be refused.

### Requirement 5: Missing, rejected and exhausted keys

**User Story:** As a GM whose key ran out of credit, I want Nornis to tell me, not to fail
quietly or keep retrying.

#### Acceptance Criteria

1. With no key and no trial left, an AI action SHALL be refused before any call, with "Add an
   AI key on Profile to use this" and a link.
2. A key the provider rejects (401 or 403) SHALL be marked as failing its check. The work
   SHALL stop with "Your OpenAI key was rejected" (or the provider in question) and a link to
   Profile.
3. A key whose account has no credit or quota left SHALL be treated differently from rate
   limiting: the work SHALL stop with "Your OpenAI account is out of credit", and SHALL NOT be
   retried by the queue.
4. Rate limiting SHALL stay a transient failure, retried on the existing backoff.
5. A source whose extraction stopped for one of these three reasons SHALL show the reason and
   a link to Profile. When the key holder saves a key that passes its check, those sources
   SHALL be queued again.
6. The status page's AI check SHALL NOT read a user's missing, rejected or exhausted key as an
   outage. It SHALL judge each provider only on calls that reached it and failed on its side.

### Requirement 6: Budgets protect the GM

**User Story:** As a GM paying for my own AI, I still want a cap against a runaway loop.

#### Acceptance Criteria

1. The daily world budget, the monthly Prep pool and the public Ask cap SHALL keep working as
   they do today, on whatever pays for the call.
2. The Costs tab SHALL call its figures **estimates**, SHALL say that the provider's bill is
   the real figure, and SHALL show which key, or the trial, paid for each line.
3. Every `AiUsageRecord` SHALL record the provider, the catalog model, what paid (a key or the
   trial) and the key holder.
4. The cost of a call SHALL come from the model catalog, or from the price the user entered
   for an OpenAI-compatible model. A model with no price SHALL NOT be callable.

### Requirement 7: Providers behave the same to the rest of Nornis

**User Story:** As Dave, I want extraction, Ask and Prep to work the same way whichever
provider runs them.

#### Acceptance Criteria

1. Every structured response (extraction, audit, digest and the rest) SHALL use a schema that
   all four providers accept in their strictest mode.
2. Every failure SHALL reach the application in the existing taxonomy (timeout, parse, HTTP),
   plus three new outcomes: key missing, key rejected, out of credit.
3. A model SHALL enter the catalog for Extraction only after the extraction eval has run on it
   and Dave has accepted the result.
4. The Application layer SHALL NOT reference any provider's SDK.

## Staging

- **Keys per world.** A GM who wants one campaign billed to another account can't do that.
  Easy to add later as an override on the world; nothing here blocks it.
- **Image generation** is not used by anything yet. Feature 38 (cover art) builds against the
  same resolver, as its own task; this feature only makes sure the shapes allow it.
- **Embeddings** keep running on Nornis's own Azure key until feature 31's Phase H deletes the
  upload index, which is the only thing that embeds. Embeddings are tiny, and after Phase H
  there are none, so they get no AI task and no user choice.
- **Paying Nornis** (a subscription instead of a key) is out of scope.
- **Tool use** (the model calling Nornis functions) is not used anywhere yet, so provider
  differences in it are not handled.
