-- Company-scoped Zarr payment ledger; independent from account-scoped Kaliia purchases.
-- Sales remain disabled until pricing, receipts and approval UI are verified.
create table if not exists public.zarr_company_payment_requests (
 id uuid primary key default gen_random_uuid(),
 company_id uuid not null references public.zarr_company_bindings(company_id) on delete restrict,
 auth_user_id uuid not null references auth.users(id) on delete restrict,
 product_id text not null default 'zarr' check(product_id='zarr'),
 duration_months smallint not null check(duration_months in (12)),
 amount_minor bigint not null check(amount_minor>0),
 currency text not null check(currency in ('IRR','IRT')),
 receipt_path text not null check(length(receipt_path) between 8 and 500),
 receipt_mime text not null check(receipt_mime in ('image/png','image/jpeg','application/pdf')),
 status text not null default 'pending' check(status in ('pending','approved','rejected')),
 submitted_at timestamptz not null default now(),
 reviewed_at timestamptz,
 reviewed_by uuid references auth.users(id),
 note text check(note is null or length(note)<=500),
 constraint zarr_company_payment_review_consistency check(
   (status='pending' and reviewed_at is null and reviewed_by is null)
   or (status in ('approved','rejected') and reviewed_at is not null and reviewed_by is not null)
 )
);
create index if not exists zarr_company_payments_company_idx
 on public.zarr_company_payment_requests(company_id, submitted_at desc);
create unique index if not exists zarr_company_one_pending_payment_idx
 on public.zarr_company_payment_requests(company_id) where status='pending';
alter table public.zarr_company_payment_requests enable row level security;
revoke all on table public.zarr_company_payment_requests from public, anon, authenticated;
grant select, insert, update on table public.zarr_company_payment_requests to service_role;

create or replace function public.zarr_approve_company_payment(
 p_payment_id uuid, p_admin_id uuid, p_approve boolean, p_note text default null
) returns jsonb language plpgsql security definer set search_path='' as $$
declare request_row public.zarr_company_payment_requests%rowtype;
        binding_row public.zarr_company_bindings%rowtype;
        entitlement_row public.zarr_company_entitlements%rowtype;
        next_expiry timestamptz;
begin
 if p_payment_id is null or p_admin_id is null or p_approve is null then
   raise exception 'invalid_review_request' using errcode='22023';
 end if;
 if not exists(select 1 from public.zurvan_admin_users
    where auth_user_id=p_admin_id and active=true and role='superadmin') then
   raise exception 'admin_required' using errcode='42501';
 end if;
 select * into request_row from public.zarr_company_payment_requests
   where id=p_payment_id for update;
 if not found then raise exception 'payment_not_found' using errcode='22023'; end if;
 if request_row.status<>'pending' then
   raise exception 'already_reviewed' using errcode='23505';
 end if;
 select * into binding_row from public.zarr_company_bindings
  where company_id=request_row.company_id for update;
 if not found or binding_row.state<>'active' or
   binding_row.auth_user_id<>request_row.auth_user_id then
   raise exception 'company_binding_changed' using errcode='42501';
 end if;
 if p_approve then
   if not exists(select 1 from public.zurvan_products
     where id='zarr' and enabled=true and sales_enabled=true) then
     raise exception 'product_not_for_sale' using errcode='42501';
   end if;
   select * into entitlement_row from public.zarr_company_entitlements
     where company_id=request_row.company_id for update;
   if found and entitlement_row.entitlement_type='lifetime' and
     entitlement_row.status='active' then
     raise exception 'lifetime_unchanged' using errcode='22023';
   end if;
   next_expiry:=greatest(now(),coalesce(entitlement_row.expires_at,now()))
       + make_interval(months=>request_row.duration_months);
   insert into public.zarr_company_entitlements(
     company_id,entitlement_type,status,started_at,expires_at,source_payment_id)
   values(request_row.company_id,'subscription','active',now(),next_expiry,p_payment_id)
   on conflict(company_id) do update set
     entitlement_type='subscription',status='active',
     expires_at=excluded.expires_at,
     source_payment_id=p_payment_id,
     generation=public.zarr_company_entitlements.generation+1,
     updated_at=now();
 end if;
 update public.zarr_company_payment_requests
 set status=case when p_approve then 'approved' else 'rejected' end,
     reviewed_at=now(),reviewed_by=p_admin_id,
     note=nullif(left(btrim(coalesce(p_note,'')),500),'')
 where id=p_payment_id;
 insert into public.zarr_company_license_audit(company_id,actor_id,action,details)
 values(request_row.company_id,p_admin_id,
        case when p_approve then 'payment_approved' else 'payment_rejected' end,
        jsonb_build_object('payment_id',p_payment_id,'months',request_row.duration_months));
 return jsonb_build_object('product_id','zarr','company_id',request_row.company_id,
   'payment_id',p_payment_id,
   'status',case when p_approve then 'approved' else 'rejected' end,
   'expires_at',case when p_approve then next_expiry else null end);
end;
$$;
revoke all on function public.zarr_approve_company_payment(uuid,uuid,boolean,text) from public,anon,authenticated;
grant execute on function public.zarr_approve_company_payment(uuid,uuid,boolean,text) to service_role;
