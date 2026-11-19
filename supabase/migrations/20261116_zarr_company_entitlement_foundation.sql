-- Zarr company-scoped entitlement foundation (additive; no existing product changes).
-- Only trusted service-role calls may access the new tables/RPCs.
create table if not exists public.zarr_company_bindings (
  company_id uuid primary key,
  product_id text not null default 'zarr' check (product_id='zarr'),
  auth_user_id uuid not null references auth.users(id) on delete restrict,
  erp_owner_id uuid not null,
  state text not null default 'active' check (state in ('active','transfer_pending','suspended')),
  generation bigint not null default 1 check (generation > 0),
  linked_at timestamptz not null default now(),
  created_at timestamptz not null default now(),
  updated_at timestamptz not null default now()
);
create index if not exists zarr_company_bindings_auth_user_idx
  on public.zarr_company_bindings(auth_user_id);
alter table public.zarr_company_bindings enable row level security;
revoke all on table public.zarr_company_bindings from public, anon, authenticated;
grant select, insert, update on table public.zarr_company_bindings to service_role;

create table if not exists public.zarr_company_entitlements (
  company_id uuid primary key references public.zarr_company_bindings(company_id) on delete restrict,
  product_id text not null default 'zarr' check (product_id='zarr'),
  entitlement_type text not null check (entitlement_type in ('trial','subscription','lifetime')),
  status text not null default 'active' check (status in ('active','suspended','revoked')),
  started_at timestamptz not null default now(),
  expires_at timestamptz,
  trial_started_at timestamptz,
  trial_expires_at timestamptz,
  generation bigint not null default 1 check (generation > 0),
  source_payment_id uuid,
  created_at timestamptz not null default now(),
  updated_at timestamptz not null default now(),
  constraint zarr_entitlements_expiry_by_type check (
    (entitlement_type='lifetime' and expires_at is null)
    or (entitlement_type in ('trial','subscription') and expires_at is not null)
  ),
  constraint zarr_entitlements_trial_period check (
    (trial_started_at is null and trial_expires_at is null)
    or (trial_started_at is not null and trial_expires_at=trial_started_at+interval '45 days')
  )
);
alter table public.zarr_company_entitlements enable row level security;
revoke all on table public.zarr_company_entitlements from public, anon, authenticated;
grant select, insert, update on table public.zarr_company_entitlements to service_role;

create table if not exists public.zarr_company_license_audit (
  id bigint generated always as identity primary key,
  company_id uuid not null,
  actor_id uuid,
  action text not null check (length(action) between 3 and 80),
  details jsonb not null default '{}'::jsonb,
  created_at timestamptz not null default now()
);
create index if not exists zarr_company_license_audit_company_time_idx
 on public.zarr_company_license_audit(company_id, created_at desc);
alter table public.zarr_company_license_audit enable row level security;
revoke all on table public.zarr_company_license_audit from public, anon, authenticated;
grant select, insert on table public.zarr_company_license_audit to service_role;
grant usage, select on sequence public.zarr_company_license_audit_id_seq to service_role;

create or replace function public.zarr_bind_company(
 p_company_id uuid, p_auth_user_id uuid, p_erp_owner_id uuid
) returns jsonb language plpgsql security definer set search_path='' as $$
declare existing public.zarr_company_bindings%rowtype;
begin
 if p_company_id is null or p_auth_user_id is null or p_erp_owner_id is null then
   raise exception 'invalid_company_binding' using errcode='22023';
 end if;
 if not exists(select 1 from public.zurvan_accounts a where a.id=p_auth_user_id and a.status='active') then
   raise exception 'license_account_inactive' using errcode='42501';
 end if;
 insert into public.zarr_company_bindings(company_id,auth_user_id,erp_owner_id)
 values (p_company_id,p_auth_user_id,p_erp_owner_id)
 on conflict (company_id) do nothing;
 select * into existing from public.zarr_company_bindings where company_id=p_company_id for update;
 if existing.auth_user_id<>p_auth_user_id then
   raise exception 'company_transfer_required' using errcode='23505';
 end if;
 if existing.state<>'active' then
   raise exception 'company_binding_not_active' using errcode='42501';
 end if;
 if existing.erp_owner_id<>p_erp_owner_id then
   raise exception 'company_owner_verification_required' using errcode='42501';
 end if;
 insert into public.zarr_company_license_audit(company_id,actor_id,action)
 select p_company_id,p_auth_user_id,'company_linked'
 where not exists(select 1 from public.zarr_company_license_audit
                  where company_id=p_company_id and action='company_linked');
 return jsonb_build_object('company_id',p_company_id,'product_id','zarr',
   'auth_user_id',existing.auth_user_id,'state',existing.state,'generation',existing.generation);
end;
$$;
revoke all on function public.zarr_bind_company(uuid,uuid,uuid) from public,anon,authenticated;
grant execute on function public.zarr_bind_company(uuid,uuid,uuid) to service_role;

create or replace function public.zarr_start_company_trial(
 p_company_id uuid, p_auth_user_id uuid
) returns jsonb language plpgsql security definer set search_path='' as $$
declare b public.zarr_company_bindings%rowtype;
        e public.zarr_company_entitlements%rowtype;
        started timestamptz;
begin
 select * into b from public.zarr_company_bindings where company_id=p_company_id for update;
 if not found or b.auth_user_id is distinct from p_auth_user_id or b.state<>'active' then
   raise exception 'company_binding_not_active' using errcode='42501';
 end if;
 select * into e from public.zarr_company_entitlements where company_id=p_company_id for update;
 if not found then
   started:=now();
   insert into public.zarr_company_entitlements(
     company_id,entitlement_type,status,started_at,expires_at,trial_started_at,trial_expires_at
   ) values(p_company_id,'trial','active',started,started+interval '45 days',started,started+interval '45 days')
   returning * into e;
   insert into public.zarr_company_license_audit(company_id,actor_id,action)
   values(p_company_id,p_auth_user_id,'trial_started');
 end if;
 return jsonb_build_object('product_id','zarr','company_id',e.company_id,
   'type',e.entitlement_type,'status',
     case when e.status='active' and e.expires_at is not null and e.expires_at<=now()
       then 'expired' else e.status end,
   'expires_at',e.expires_at,'trial_started_at',e.trial_started_at,
   'trial_expires_at',e.trial_expires_at,'generation',e.generation,
   'can_write',e.status='active' and (e.entitlement_type='lifetime' or e.expires_at>now()));
end;
$$;
revoke all on function public.zarr_start_company_trial(uuid,uuid) from public,anon,authenticated;
grant execute on function public.zarr_start_company_trial(uuid,uuid) to service_role;

create or replace function public.zarr_company_license_status(
 p_company_id uuid, p_auth_user_id uuid
) returns jsonb language plpgsql security definer set search_path='' as $$
declare b public.zarr_company_bindings%rowtype;
        e public.zarr_company_entitlements%rowtype;
begin
 select * into b from public.zarr_company_bindings where company_id=p_company_id;
 if not found or b.auth_user_id is distinct from p_auth_user_id or b.state<>'active' then
   raise exception 'company_binding_not_active' using errcode='42501';
 end if;
 select * into e from public.zarr_company_entitlements where company_id=p_company_id;
 if not found then
   return jsonb_build_object('product_id','zarr','company_id',p_company_id,
     'status','trial_available','can_write',false,'trial_available',true);
 end if;
 return jsonb_build_object('product_id','zarr','company_id',p_company_id,
   'status',case when e.status='active' and e.expires_at is not null and e.expires_at<=now() then 'expired' else e.status end,
   'type',e.entitlement_type,'expires_at',e.expires_at,'generation',e.generation,
   'trial_available',false,'can_write',e.status='active' and
     (e.entitlement_type='lifetime' or e.expires_at>now()));
end;
$$;
revoke all on function public.zarr_company_license_status(uuid,uuid) from public,anon,authenticated;
grant execute on function public.zarr_company_license_status(uuid,uuid) to service_role;
