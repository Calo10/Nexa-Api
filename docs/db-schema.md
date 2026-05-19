Database: NexaCoreDB-dev
Engine: SQL Server

Tables:

users
- id (uniqueidentifier, PK)
- email (nvarchar)
- email_normalized (computed, persisted)
- full_name (nvarchar, null)
- avatar_url (nvarchar, null)
- status (nvarchar: active|disabled)
- email_verified_at (datetimeoffset, null)
- password_hash (nvarchar(255), null)
- password_set_at (datetimeoffset, null)
- failed_login_count (int, default 0)
- locked_until (datetimeoffset, null)
- created_at (datetimeoffset)
- updated_at (datetimeoffset)
- deleted_at (datetimeoffset, null)

organizations
- id (uniqueidentifier, PK)
- name (nvarchar)
- slug (nvarchar, unique)
- status (nvarchar: active|suspended)
- created_at
- updated_at
- deleted_at

org_settings
- org_id (PK, FK -> organizations.id)
- logo_url (nvarchar, null)
- timezone (nvarchar)

org_memberships
- id (uniqueidentifier, PK)
- org_id (FK)
- user_id (FK)
- role (owner|admin|member)
- status (active|invited|removed)
- joined_at
- created_at

org_invites
- id (uniqueidentifier, PK)
- org_id (FK)
- email
- email_normalized (computed)
- role
- invited_by_user_id (FK users.id)
- token_hash
- expires_at
- accepted_at
- created_at

magic_links
- id (uniqueidentifier, PK)
- email
- email_normalized
- user_id (FK users.id, null)
- token_hash
- expires_at
- consumed_at
- created_at

password_reset_tokens
- id (uniqueidentifier, PK)
- user_id (FK users.id)
- token_hash
- expires_at
- consumed_at
- created_at

sessions
- id (uniqueidentifier, PK)
- user_id (FK users.id)
- org_id (FK organizations.id, null)
- refresh_token_hash
- user_agent
- ip
- expires_at
- revoked_at
- created_at

plans
- id (uniqueidentifier, PK)
- key
- name
- interval (month|year)
- currency
- price_cents
- trial_days
- is_active
- created_at

subscriptions
- id (uniqueidentifier, PK)
- org_id (FK organizations.id, UNIQUE)
- plan_id (FK plans.id)
- status (trialing|active|past_due|canceled|unpaid)
- stripe_customer_id
- stripe_subscription_id
- current_period_start
- current_period_end
- cancel_at_period_end
- canceled_at
- created_at
- updated_at

features
- key (PK)
- description
- created_at

plan_features
- plan_id (FK plans.id)
- feature_key (FK features.key)
- value_json (json)

org_feature_overrides
- org_id (FK organizations.id)
- feature_key (FK features.key)
- value_json (json)
- created_at

audit_log
- id (uniqueidentifier, PK)
- org_id (FK organizations.id, null)
- actor_user_id (FK users.id, null)
- action
- entity_type
- entity_id
- metadata (json)
- ip
- user_agent
- created_at
