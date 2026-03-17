

create extension if not exists "pgcrypto";

-----------------------------------------------------------
-- USER PROFILES
-----------------------------------------------------------
create table if not exists user_profiles (
id uuid primary key references auth.users(id) on delete cascade,
display_name text,
avatar_url text,
role text default 'User',
created_at timestamptz default now()
);

alter table user_profiles enable row level security;

create policy profile_select_own
on user_profiles
for select
using (auth.uid() = id);

create policy profile_insert_own
on user_profiles
for insert
with check (auth.uid() = id);

create policy profile_update_own
on user_profiles
for update
using (auth.uid() = id);

-----------------------------------------------------------
-- PROJECTS
-----------------------------------------------------------
create table if not exists projects (
id uuid primary key default gen_random_uuid(),
user_id uuid not null default auth.uid(),
name text not null,
description text default '',
status text default 'Active',
created_at timestamptz default now()
);

alter table projects enable row level security;

create policy projects_select_own
on projects
for select
using (user_id = auth.uid());

create policy projects_insert_own
on projects
for insert
with check (user_id = auth.uid());

create policy projects_update_own
on projects
for update
using (user_id = auth.uid());

create policy projects_delete_own
on projects
for delete
using (user_id = auth.uid());

-----------------------------------------------------------
-- TASK ITEMS
-----------------------------------------------------------
create table if not exists task_items (
id uuid primary key default gen_random_uuid(),
project_id uuid references projects(id) on delete cascade,
title text not null,
status text default 'Pending',
priority text default 'Normal',
assigned_user uuid,
due_date timestamptz,
notes text default '',
is_completed boolean default false,
created_at timestamptz default now()
);

alter table task_items enable row level security;

create policy tasks_access_project_owner
on task_items
for all
using (
exists (
select 1 from projects p
where p.id = project_id
and p.user_id = auth.uid()
)
);

-----------------------------------------------------------
-- PROJECT NOTES
-----------------------------------------------------------
create table if not exists project_notes (
id uuid primary key default gen_random_uuid(),
project_id uuid references projects(id) on delete cascade,
content text default '',
created_at timestamptz default now()
);

alter table project_notes enable row level security;

create policy notes_access_project_owner
on project_notes
for all
using (
exists (
select 1 from projects p
where p.id = project_id
and p.user_id = auth.uid()
)
);

-----------------------------------------------------------
-- PROJECT ACTIVITIES
-----------------------------------------------------------
create table if not exists project_activities (
id uuid primary key default gen_random_uuid(),
project_id uuid references projects(id) on delete cascade,
user_id uuid default auth.uid(),
action text,
description text,
created_at timestamptz default now()
);

alter table project_activities enable row level security;

create policy activities_access_project_owner
on project_activities
for all
using (
exists (
select 1 from projects p
where p.id = project_id
and p.user_id = auth.uid()
)
);

-----------------------------------------------------------
-- API KEY VAULT
-----------------------------------------------------------
create table if not exists api_key_items (
id uuid primary key default gen_random_uuid(),
user_id uuid default auth.uid(),
name text,
provider text,
encrypted_value text,
initialization_vector text,
description text,
tags text,
created_at timestamptz default now()
);

alter table api_key_items enable row level security;

create policy api_keys_access_own
on api_key_items
for all
using (user_id = auth.uid());

-----------------------------------------------------------
-- WORKFLOWS
-----------------------------------------------------------
create table if not exists workflow_items (
id uuid primary key default gen_random_uuid(),
user_id uuid default auth.uid(),
title text,
description text,
category text,
file_path text,
created_at timestamptz default now()
);

alter table workflow_items enable row level security;

create policy workflows_access_own
on workflow_items
for all
using (user_id = auth.uid());

-----------------------------------------------------------
-- EXTERNAL TOOLS / PLUGINS
-----------------------------------------------------------
create table if not exists external_tools (
id uuid primary key default gen_random_uuid(),
user_id uuid default auth.uid(),
name text,
api_endpoint text,
auth_type text,
headers jsonb,
description text,
created_at timestamptz default now()
);

alter table external_tools enable row level security;

create policy tools_access_own
on external_tools
for all
using (user_id = auth.uid());

-----------------------------------------------------------
-- MEETING LINKS
-----------------------------------------------------------
create table if not exists meeting_links (
id uuid primary key default gen_random_uuid(),
user_id uuid default auth.uid(),
title text,
platform text,
meeting_url text,
notes text,
created_at timestamptz default now()
);

alter table meeting_links enable row level security;

create policy meetings_access_own
on meeting_links
for all
using (user_id = auth.uid());

-----------------------------------------------------------
-- APP LOGS
-----------------------------------------------------------
create table if not exists app_logs (
id uuid primary key default gen_random_uuid(),
user_id uuid default auth.uid(),
timestamp timestamptz default now(),
action text,
status text,
details text
);

alter table app_logs enable row level security;

create policy logs_access_own
on app_logs
for all
using (user_id = auth.uid());

-----------------------------------------------------------
-- AI USAGE RECORDS
-----------------------------------------------------------
create table if not exists ai_usage_records (
id uuid primary key default gen_random_uuid(),
user_id uuid default auth.uid(),
provider text,
tokens_used int default 0,
prompt_type text,
created_at timestamptz default now()
);

alter table ai_usage_records enable row level security;

create policy ai_usage_access_own
on ai_usage_records
for all
using (user_id = auth.uid());

-----------------------------------------------------------
-- PROJECT REPORTS
-----------------------------------------------------------
create table if not exists project_reports (
id uuid primary key default gen_random_uuid(),
user_id uuid default auth.uid(),
project_name text,
completed_tasks jsonb default '[]'::jsonb,
next_steps jsonb default '[]'::jsonb,
blockers jsonb default '[]'::jsonb,
report_date timestamptz default now()
);

alter table project_reports enable row level security;

create policy reports_access_own
on project_reports
for all
using (user_id = auth.uid());

-----------------------------------------------------------
-- APP SETTINGS
-----------------------------------------------------------
create table if not exists app_settings (
id uuid primary key default gen_random_uuid(),
user_id uuid default auth.uid(),
default_ai_provider text default 'OpenAI',
theme text default 'Dark',
auto_update_enabled boolean default true,
notifications_enabled boolean default true,
default_project_view text default 'List',
last_backup_date timestamptz
);

alter table app_settings enable row level security;

create policy settings_access_own
on app_settings
for all
using (user_id = auth.uid());

-- Projects table definition
CREATE TABLE IF NOT EXISTS projects (
    id uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    user_id uuid NOT NULL,
    name text NOT NULL,
    description text NOT NULL DEFAULT '',
    status text NOT NULL DEFAULT 'Active',
    created_at timestamp with time zone NOT NULL DEFAULT now()
);

-- app_logs table fix: ensure created_at column
ALTER TABLE app_logs
ADD COLUMN IF NOT EXISTS created_at timestamp with time zone NOT NULL DEFAULT now();

-- RLS policies for projects
ALTER TABLE projects ENABLE ROW LEVEL SECURITY;

DROP POLICY IF EXISTS "Users can insert own projects" ON projects;
DROP POLICY IF EXISTS "Users can insert their projects" ON projects;
DROP POLICY IF EXISTS insert_own_projects ON projects;
DROP POLICY IF EXISTS select_own_projects ON projects;

CREATE POLICY insert_own_projects
ON projects
FOR INSERT
TO authenticated
WITH CHECK (auth.uid() = user_id);

CREATE POLICY select_own_projects
ON projects
FOR SELECT
TO authenticated
USING (auth.uid() = user_id);
