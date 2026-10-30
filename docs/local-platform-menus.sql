-- 本地 MOM 菜单整理：按当前确认的目录表补齐规划菜单。
--
-- 使用方式：连接本地 ZX_IDENTITY schema 后执行本文件（F5）。
-- 脚本可重复执行：已有规划目录会按编码复用并调整到目标父级，缺失目录才会新增。
-- 目录暂时没有真实页面组件，前端会统一进入待建设预览页。

DECLARE
    v_platform_id RAW(16);
    v_platform_build_id RAW(16);
    v_modules_id RAW(16);
    v_data_sources_id RAW(16);
    v_production_id RAW(16);
    v_quality_id RAW(16);
    v_npi_id RAW(16);
    v_mp_id RAW(16);
    v_equipment_id RAW(16);
    v_layout_id RAW(16);
    v_business_id RAW(16);
    v_standard_id RAW(16);
    v_it_id RAW(16);
    v_child_id RAW(16);

    PROCEDURE ensure_directory(
        p_key                    IN VARCHAR2,
        p_parent_id              IN RAW,
        p_title                  IN NVARCHAR2,
        p_title_en               IN NVARCHAR2,
        p_icon                   IN NVARCHAR2,
        p_sort                   IN NUMBER,
        p_launcher_tone          IN NVARCHAR2,
        p_launcher_description   IN NVARCHAR2,
        p_launcher_description_en IN NVARCHAR2,
        p_id                     OUT RAW
    ) IS
        v_code NVARCHAR2(100) := 'menu_' || p_key;
        v_count NUMBER;
    BEGIN
        BEGIN
            SELECT id INTO p_id FROM ZX_IDENTITY_MENU
             WHERE code = v_code AND is_deleted = 0;
        EXCEPTION
            WHEN NO_DATA_FOUND THEN
                BEGIN
                    SELECT id INTO p_id FROM ZX_IDENTITY_MENU
                     WHERE kind = 'directory' AND title = p_title AND is_deleted = 0
                       AND ((parent_id = p_parent_id)
                            OR (parent_id IS NULL AND p_parent_id IS NULL));
                EXCEPTION
                    WHEN NO_DATA_FOUND THEN
                        SELECT COUNT(*) INTO v_count FROM ZX_IDENTITY_MENU WHERE code = v_code;
                        IF v_count > 0 THEN v_code := 'menu_' || RAWTOHEX(SYS_GUID()); END IF;
                        p_id := SYS_GUID();
                        INSERT INTO ZX_IDENTITY_MENU (
                            id, parent_id, code, kind, title, title_en, icon,
                            path, component, permission_code,
                            launcher_tone, launcher_description, launcher_description_en,
                            sort, keep_alive, show_in_menu, show_in_tags, affix,
                            is_enabled, is_built_in,
                            created_at_utc, created_by_user_id,
                            updated_at_utc, updated_by_user_id, is_deleted
                        ) VALUES (
                            p_id, p_parent_id, v_code, 'directory', p_title, p_title_en, p_icon,
                            NULL, NULL, NULL,
                            p_launcher_tone, p_launcher_description, p_launcher_description_en,
                            p_sort, 0, 1, 1, 0,
                            1, 0, SYSTIMESTAMP, NULL, NULL, NULL, 0);
                        DBMS_OUTPUT.PUT_LINE('已新增目录：' || p_title || '（' || v_code || '）');
                END;
        END;

        -- 仅维护本脚本使用的自定义目录；内置系统菜单不修改。
        UPDATE ZX_IDENTITY_MENU
           SET parent_id = p_parent_id, title = p_title, title_en = p_title_en,
               icon = p_icon, launcher_tone = p_launcher_tone,
               launcher_description = p_launcher_description,
               launcher_description_en = p_launcher_description_en,
               sort = p_sort, is_enabled = 1
         WHERE id = p_id AND is_built_in = 0 AND is_deleted = 0;
    END ensure_directory;

    PROCEDURE retire_directory(p_code IN VARCHAR2) IS
    BEGIN
        -- 这些编码来自之前的规划清单，现已不在当前目录表中；只停用自定义节点。
        UPDATE ZX_IDENTITY_MENU SET is_enabled = 0, is_deleted = 1
         WHERE code = p_code AND is_built_in = 0 AND is_deleted = 0;
    END retire_directory;
BEGIN
    -- 平台管理：工作台由前端固定入口注入，系统管理沿用内置菜单。
    ensure_directory('platform', NULL, '平台管理', 'Platform management', 'svg:lucide-layers', 10,
        'primary', '平台基础能力与系统支撑', 'Platform services and system support', v_platform_id);
    ensure_directory('platform_build', v_platform_id, '平台建设', 'Platform construction',
        'svg:lucide-blocks', 20, 'primary', '平台模块与数据源配置',
        'Platform modules and data source configuration', v_platform_build_id);
    ensure_directory('modules', v_platform_build_id, '模块管理', 'Module management',
        'svg:lucide-blocks', 10, 'primary', '统一管理平台模块。', 'Manage platform modules.', v_modules_id);
    ensure_directory('data_sources', v_platform_build_id, '数据源管理', 'Data source management',
        'svg:lucide-database', 20, 'primary', '集中管理外部系统的数据源与连接配置。',
        'Manage data sources and connection settings for external systems.', v_data_sources_id);

    -- 生产管理：只保留当前确认的一键交接班、知识库和智能看板。
    ensure_directory('production', NULL, '生产管理', 'Production management', 'svg:lucide-cog', 20,
        'warning', '生产运营与现场作业管理。', 'Production operations and shop floor work.', v_production_id);
    ensure_directory('production_handover', v_production_id, '一键交接班', 'One-click handover',
        'svg:lucide-arrow-right-left', 10, 'warning', '快速完成班次交接与未结事项确认。',
        'Complete shift handover and confirm open items.', v_child_id);
    ensure_directory('production_knowledge', v_production_id, '知识库', 'Knowledge base',
        'svg:lucide-library-big', 20, 'violet', '沉淀生产现场知识与处理经验。',
        'Capture production knowledge and troubleshooting experience.', v_child_id);
    ensure_directory('production_dashboard', v_production_id, '智能看板', 'Smart dashboard',
        'svg:lucide-layout-dashboard', 30, 'primary', '集中展示生产运营关键指标。',
        'Show key production operation indicators.', v_child_id);

    -- 质量管理。
    ensure_directory('qms', NULL, '质量管理', 'Quality management', 'svg:lucide-badge-check', 30,
        'success', '质量管理与不良处理。', 'Quality management and defect handling.', v_quality_id);
    ensure_directory('quality_qms', v_quality_id, 'QMS', 'QMS', 'svg:lucide-badge-check', 10,
        'success', '质量管理系统建设中。', 'QMS functionality is being built.', v_child_id);

    -- NPI 管理。
    ensure_directory('npi', NULL, 'NPI管理', 'NPI management', 'svg:lucide-rocket', 40,
        'warning', '新产品导入与工程协同。', 'New product introduction and engineering collaboration.', v_npi_id);
    ensure_directory('npi_goflow', v_npi_id, 'Go-flow', 'Go-flow', 'svg:lucide-blocks', 10,
        'warning', 'NPI 流程协同入口。', 'NPI workflow collaboration.', v_child_id);
    ensure_directory('npi_materials', v_npi_id, '物料管理', 'Material management', 'svg:lucide-package', 20,
        'warning', 'NPI 物料准备与状态管理。', 'NPI material preparation and status management.', v_child_id);
    ensure_directory('npi_tools', v_npi_id, '治工具管理', 'Tooling management', 'svg:lucide-wrench', 30,
        'warning', 'NPI 治具与工具准备管理。', 'NPI tooling and fixture preparation.', v_child_id);
    ensure_directory('npi_yield', v_npi_id, '良率', 'Yield', 'svg:lucide-chart-line', 40,
        'success', '跟踪 NPI 阶段良率变化。', 'Track yield during NPI.', v_child_id);

    -- MP 管理。
    ensure_directory('mp', NULL, 'MP管理', 'MP management', 'svg:lucide-chart-line', 50,
        'warning', '量产良率与产能分析。', 'Mass production yield and capacity analysis.', v_mp_id);
    ensure_directory('mp_yield', v_mp_id, '良率', 'Yield', 'svg:lucide-chart-line', 10,
        'success', '分析量产良率与变化趋势。', 'Analyze mass production yield and trends.', v_child_id);

    -- 设备、Layout、经管和标准管理。
    ensure_directory('equipment', NULL, '设备管理', 'Equipment management', 'svg:lucide-cpu', 60,
        'success', '设备运行与维护管理。', 'Equipment operation and maintenance.', v_equipment_id);
    ensure_directory('equipment_reports', v_equipment_id, '设备管理', 'Equipment management',
        'svg:lucide-clipboard-list', 10, 'success', '设备管理功能建设中。',
        'Equipment management functionality is being built.', v_child_id);
    ensure_directory('layout', NULL, 'Layout管理', 'Layout management', 'svg:lucide-layout-dashboard', 70,
        'primary', '现场布局与工艺规划。', 'Shop floor layout and process planning.', v_layout_id);
    ensure_directory('layout_planning', v_layout_id, '建设中', 'In progress', 'svg:lucide-layout-dashboard', 10,
        'primary', 'Layout 管理功能建设中。', 'Layout management functionality is being built.', v_child_id);
    ensure_directory('business', NULL, '经管管理', 'Business management', 'svg:lucide-chart-column', 80,
        'success', '经营分析与绩效管理。', 'Business analysis and performance management.', v_business_id);
    ensure_directory('business_profit', v_business_id, '盈亏报表', 'Profit and loss reports',
        'svg:lucide-chart-column', 10, 'success', '汇总收入、成本与利润变化。',
        'Summarize revenue, costs and profit changes.', v_child_id);
    ensure_directory('standardization', NULL, '标准管理', 'Standards management', 'svg:lucide-file-check', 90,
        'primary', '标准文档、作业指导与规范管理。', 'Standards, work instructions and specifications.', v_standard_id);
    ensure_directory('standardization_standard', v_standard_id, '标准管理', 'Standards management',
        'svg:lucide-file-check', 10, 'primary', '标准体系建设中。', 'Standards functionality is being built.', v_child_id);

    -- IT 智能服务：原先的多个一级目录收拢到这里。
    ensure_directory('it', NULL, 'IT智能服务', 'IT intelligent services', 'svg:lucide-headset', 100,
        'violet', 'IT 服务与智能化应用。', 'IT services and intelligent applications.', v_it_id);
    ensure_directory('it_tickets', v_it_id, 'IT服务', 'IT services', 'svg:lucide-ticket', 10,
        'violet', '统一受理 IT 服务请求。', 'Receive IT service requests.', v_child_id);
    ensure_directory('knowledge', v_it_id, 'AI知识', 'AI knowledge', 'svg:lucide-library-big', 20,
        'violet', '沉淀 SOP 与异常处理知识。', 'Capture SOP and troubleshooting knowledge.', v_child_id);
    ensure_directory('validation', v_it_id, '程序验证', 'Program validation', 'svg:lucide-shield-check', 30,
        'warning', 'MES 程序验证与测试。', 'MES program validation and testing.', v_child_id);
    ensure_directory('logs', v_it_id, '智能收集', 'Smart collection', 'svg:lucide-scroll-text', 40,
        'primary', '日志收集与问题定位。', 'Log collection and issue investigation.', v_child_id);
    ensure_directory('tools', v_it_id, '智能工具', 'Intelligent tools', 'svg:lucide-sparkles', 50,
        'violet', '智能报表、异常查询与 AI 助手。', 'Intelligent reports, exception data and AI assistant.', v_child_id);
    ensure_directory('programs', v_it_id, '产线程序管理', 'Line programs', 'svg:lucide-folder-code', 60,
        'warning', '产线程序配置与站点管理。', 'Line program configuration and station management.', v_child_id);
    ensure_directory('kpi', v_it_id, 'MES KPI管理', 'MES KPIs', 'svg:lucide-gauge', 70,
        'success', 'MES 程序异常与处理效率分析。', 'MES program exceptions and resolution efficiency.', v_child_id);

    -- 清理上一个版本脚本带入、但不在当前目录表中的多余规划项。
    retire_directory('menu_knowledge_sop');
    retire_directory('menu_knowledge_troubleshooting');
    retire_directory('menu_validation_mes');
    retire_directory('menu_logs_search');
    retire_directory('menu_logs_interfaces');
    retire_directory('menu_logs_errors');
    retire_directory('menu_tools_reports');
    retire_directory('menu_tools_exceptions');
    retire_directory('menu_tools_assistant');
    retire_directory('menu_programs_configuration');
    retire_directory('menu_programs_stations');
    retire_directory('menu_kpi_program_errors');
    retire_directory('menu_kpi_resolution');
    retire_directory('menu_kpi_non_mes');
    retire_directory('menu_production_reports');
    retire_directory('menu_production_stocktake');
    retire_directory('menu_production_traceability');
    retire_directory('menu_production_smt');
    retire_directory('menu_equipment_kpi');
    retire_directory('menu_kpi_system');

    COMMIT;
END;
/
