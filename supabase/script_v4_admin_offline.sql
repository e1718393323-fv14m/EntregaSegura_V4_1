-- =====================================================================
-- Entrega Segura · V4: rol ADMINISTRADOR y recuperación de contraseña
-- Proyecto Supabase: https://psxxnkjmmlqsbiyygxnj.supabase.co
--
-- Ejecutar DESPUÉS de script_entregasegura.sql (Supabase > SQL Editor > Run).
-- No borra datos y se puede ejecutar varias veces.
-- Si vuelve a ejecutar script_entregasegura.sql, ejecute este script otra vez.
--
-- ANTES: crear en Authentication > Users el usuario admin@entregasegura.com
--        (con "Auto Confirm User").
-- =====================================================================

create extension if not exists pgcrypto with schema extensions;

-- ---------------------------------------------------------------
-- 1. Rol ADMINISTRADOR y usuario administrador
-- ---------------------------------------------------------------
alter table uis.roles drop constraint if exists roles_nombre_check;
alter table uis.roles add constraint roles_nombre_check
    check (nombre in ('CONDUCTOR', 'SUPERVISOR', 'ADMINISTRADOR'));
insert into uis.roles (nombre) values ('ADMINISTRADOR') on conflict (nombre) do nothing;

-- true cuando el administrador asignó una clave temporal: el usuario debe cambiarla al entrar
alter table uis.conductores add column if not exists debe_cambiar_clave boolean not null default false;

insert into uis.conductores (email, nombres, cedula, telefono, rol_id, vehiculo_id)
select 'admin@entregasegura.com', 'Administrador del Sistema', '1700000001', '0990000000', r.id, null
  from uis.roles r where r.nombre = 'ADMINISTRADOR'
on conflict (email) do update set rol_id = excluded.rol_id, nombres = excluded.nombres;

create or replace function uis.es_administrador()
returns boolean language sql stable security definer set search_path = '' as $$
    select exists (select 1 from uis.conductores c join uis.roles r on r.id = c.rol_id
                   where c.email = auth.email() and r.nombre = 'ADMINISTRADOR');
$$;
revoke all on function uis.es_administrador() from public;
grant execute on function uis.es_administrador() to authenticated;

-- El administrador ve a todos los usuarios
drop policy if exists conductores_ver on uis.conductores;
create policy conductores_ver on uis.conductores for select to authenticated
    using (email = auth.email() or uis.es_supervisor() or uis.es_administrador());

-- ---------------------------------------------------------------
-- 2. Solicitudes de "Olvidé mi contraseña" y auditoría
-- ---------------------------------------------------------------
create table if not exists uis.solicitudes_clave (
    id           bigint generated always as identity primary key,
    email        text not null,
    fecha        timestamptz not null default now(),
    estado       text not null default 'PENDIENTE' check (estado in ('PENDIENTE', 'ATENDIDA')),
    atendida_por text,
    atendida_en  timestamptz
);
create index if not exists solicitudes_clave_estado_idx on uis.solicitudes_clave (estado, fecha);

create table if not exists uis.auditoria_claves (
    id            bigint generated always as identity primary key,
    email_usuario text not null,
    email_admin   text not null,
    fecha         timestamptz not null default now()
);

alter table uis.solicitudes_clave enable row level security;
alter table uis.auditoria_claves  enable row level security;
grant select on uis.solicitudes_clave, uis.auditoria_claves to authenticated;   -- sin insert/update directos

drop policy if exists solicitudes_ver on uis.solicitudes_clave;
create policy solicitudes_ver on uis.solicitudes_clave for select to authenticated using (uis.es_administrador());
drop policy if exists auditoria_ver on uis.auditoria_claves;
create policy auditoria_ver on uis.auditoria_claves for select to authenticated using (uis.es_administrador());

-- ---------------------------------------------------------------
-- 3. Funciones (RPC)
-- ---------------------------------------------------------------

-- a) Desde el Login, sin sesión: registra la solicitud.
--    Responde siempre 'OK' (exista o no el correo) y no duplica solicitudes pendientes.
create or replace function uis.solicitar_recuperacion(p_email text)
returns text language plpgsql security definer set search_path = '' as $$
declare
    v_email text := lower(trim(coalesce(p_email, '')));
begin
    if position('@' in v_email) > 0
       and exists (select 1 from uis.conductores where email = v_email)
       and not exists (select 1 from uis.solicitudes_clave where email = v_email and estado = 'PENDIENTE') then
        insert into uis.solicitudes_clave (email) values (v_email);
    end if;
    return 'OK';
end;
$$;
revoke all on function uis.solicitar_recuperacion(text) from public;
grant usage on schema uis to anon;
grant execute on function uis.solicitar_recuperacion(text) to anon, authenticated;

-- b) Solo ADMINISTRADOR: asigna una contraseña temporal al usuario.
--    Cierra sus sesiones abiertas, marca que debe cambiarla y deja auditoría.
create or replace function uis.admin_restablecer_clave(p_email text, p_clave text)
returns text language plpgsql security definer set search_path = '' as $$
declare
    v_email text := lower(trim(coalesce(p_email, '')));
    v_id    uuid;
begin
    if not uis.es_administrador() then
        raise exception 'SOLO_ADMIN' using errcode = '42501';
    end if;
    if p_clave is null or length(p_clave) < 8 then
        raise exception 'CLAVE_CORTA';
    end if;

    select id into v_id from auth.users where lower(email) = v_email;
    if v_id is null then
        raise exception 'USUARIO_NO_EXISTE';
    end if;

    update auth.users
       set encrypted_password = extensions.crypt(p_clave, extensions.gen_salt('bf')),
           updated_at = now()
     where id = v_id;

    -- Cierra las sesiones abiertas del usuario (los otros teléfonos deben volver a entrar)
    begin
        delete from auth.sessions where user_id = v_id;
    exception when others then null;
    end;

    update uis.conductores set debe_cambiar_clave = true where email = v_email;
    update uis.solicitudes_clave
       set estado = 'ATENDIDA', atendida_por = auth.email(), atendida_en = now()
     where email = v_email and estado = 'PENDIENTE';
    insert into uis.auditoria_claves (email_usuario, email_admin) values (v_email, auth.email());
    return 'OK';
end;
$$;
revoke all on function uis.admin_restablecer_clave(text, text) from public;
grant execute on function uis.admin_restablecer_clave(text, text) to authenticated;

-- c) El usuario ya cambió su clave temporal
create or replace function uis.marcar_clave_cambiada()
returns text language sql security definer set search_path = '' as $$
    update uis.conductores set debe_cambiar_clave = false where email = auth.email();
    select 'OK';
$$;
revoke all on function uis.marcar_clave_cambiada() from public;
grant execute on function uis.marcar_clave_cambiada() to authenticated;

notify pgrst, 'reload schema';

-- Verificación
select c.email, r.nombre as rol, c.debe_cambiar_clave,
       exists (select 1 from auth.users u where lower(u.email) = c.email) as existe_en_auth
  from uis.conductores c join uis.roles r on r.id = c.rol_id
 order by r.nombre, c.email;
