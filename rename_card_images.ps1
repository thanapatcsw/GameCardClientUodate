# Rename card images to match imageName in cards_database.json
# Also renames corresponding .meta files

$baseDir = "D:\ProjectGameCard\GameCardClient\Assets\Resources\Card Image"

# Mapping: "old pattern" => "new name" (without extension)
$renameMap = @{
    # CPU Tier1
    "$baseDir\CPU\Tier1\transistor_red_card_*.png" = "transistor"
    "$baseDir\CPU\Tier1\logic_gate_red_card_*.png" = "logic_gate"
    "$baseDir\CPU\Tier1\alu_red_card_*.png" = "alu"
    "$baseDir\CPU\Tier1\clock_signal_red_card_*.png" = "clock_signal"
    "$baseDir\CPU\Tier1\assembler_red_card_*.png" = "assembler"
    "$baseDir\CPU\Tier1\syntax_structure_red_card_*.png" = "syntax_structure"
    "$baseDir\CPU\Tier1\microprocessor_red_card_*.png" = "microprocessor"
    "$baseDir\CPU\Tier1\pipelining_red_card_*.png" = "pipelining"
    # CPU Tier2
    "$baseDir\CPU\Tier2\task_scheduler_red_card_*.png" = "task_scheduler"
    "$baseDir\CPU\Tier2\interrupt_handler_red_card_*.png" = "interrupt_handler"
    "$baseDir\CPU\Tier2\multi_core_red_card_*.png" = "multi_core"
    "$baseDir\CPU\Tier2\multithreading_red_card_*.png" = "multithreading"
    "$baseDir\CPU\Tier2\hyper_threading_red_card_*.png" = "hyper_threading"
    "$baseDir\CPU\Tier2\gpu_red_card_*.png" = "gpu"
    # CPU Tier3
    "$baseDir\CPU\Tier3\server_farm_cpu_red_card_*.png" = "server_farm_cpu"
    "$baseDir\CPU\Tier3\neural_processing_unit_npu_red_card_*.png" = "neural_processing_unit_npu"
    "$baseDir\CPU\Tier3\quantum_processor_qpu_red_card_*.png" = "quantum_processor_qpu"
    "$baseDir\CPU\Tier3\supercomputer.png" = "supercomputer"
    # RAM Tier1
    "$baseDir\RAM\Tier1\bit_blue_card_*.png" = "bit"
    "$baseDir\RAM\Tier1\variable_blue_card_*.png" = "variable"
    "$baseDir\RAM\Tier1\pointer_blue_card_*.png" = "pointer"
    "$baseDir\RAM\Tier1\cpu_registers_blue_card_*.png" = "cpu_registers"
    "$baseDir\RAM\Tier1\sram_blue_card_*.png" = "sram"
    "$baseDir\RAM\Tier1\dram_blue_card_*.png" = "dram"
    "$baseDir\RAM\Tier1\data_structure_basics_blue_card_*.png" = "data_structure_basics"
    "$baseDir\RAM\Tier1\cache_memory_l1_blue_card_*.png" = "cache_memory_l1"
    # RAM Tier2
    "$baseDir\RAM\Tier2\memory_allocation_blue_card_*.png" = "memory_allocation"
    "$baseDir\RAM\Tier2\garbage_collection_blue_card_*.png" = "garbage_collection"
    "$baseDir\RAM\Tier2\virtual_memory_blue_card_*.png" = "virtual_memory"
    "$baseDir\RAM\Tier2\paging_swapping_blue_card_*.png" = "paging_swapping"
    "$baseDir\RAM\Tier2\cache_architecture_blue_card_*.png" = "l2l3_cache"
    "$baseDir\RAM\Tier2\buffer_overflow_protection_blue_card_*.png" = "buffer_overflow"
    # RAM Tier3
    "$baseDir\RAM\Tier3\shared_memory_model_blue_card_*.png" = "shared_memory"
    "$baseDir\RAM\Tier3\in_memory_database_blue_card_*.png" = "inmemory_db"
    "$baseDir\RAM\Tier3\distributed_cache_blue_card_*.png" = "distributed_cache"
    "$baseDir\RAM\Tier3\Holographic Memory.png" = "holographic_memory"
    # Storage Tier1
    "$baseDir\Storage\Tier1\text_file_storage_card_*.png" = "text_file"
    "$baseDir\Storage\Tier1\directory_storage_card_*.png" = "directory"
    "$baseDir\Storage\Tier1\basic_query_storage_card_*.png" = "basic_query"
    "$baseDir\Storage\Tier1\hdd_black_line_card_*.png" = "hdd"
    "$baseDir\Storage\Tier1\ssd_black_line_card_*.png" = "ssd"
    "$baseDir\Storage\Tier1\fat_formatter_black_line_card_*.png" = "fat_formatter"
    "$baseDir\Storage\Tier1\relational_database_black_line_card_*.png" = "relational_db"
    "$baseDir\Storage\Tier1\btree_indexing_black_line_card_*.png" = "btree_indexing"
    # Storage Tier2
    "$baseDir\Storage\Tier2\json_formatting_black_line_card_*.png" = "json_formatting"
    "$baseDir\Storage\Tier2\Data Compression.png" = "data_compression"
    "$baseDir\Storage\Tier2\SQL Standards.png" = "sql_standards"
    "$baseDir\Storage\Tier2\NoSQL Database.png" = "nosql_database"
    "$baseDir\Storage\Tier2\RAID Array.png" = "raid_array"
    "$baseDir\Storage\Tier2\Snapshot Recovery.png" = "snapshot_recovery"
    # Storage Tier3
    "$baseDir\Storage\Tier3\Data Warehouse.png" = "data_warehouse"
    "$baseDir\Storage\Tier3\Distributed File System.png" = "distributed_file_system"
    "$baseDir\Storage\Tier3\Cloud Object Storage.png" = "cloud_object_storage"
    "$baseDir\Storage\Tier3\Screenshot 2026-04-28 064416.png" = "big_data_center"
    # Network Tier1
    "$baseDir\Network\Tier1\lan_cable_network_card_*.png" = "lan_cable"
    "$baseDir\Network\Tier1\mac_address_network_card_*.png" = "mac_address"
    "$baseDir\Network\Tier1\network_hub_network_card_*.png" = "network_hub"
    "$baseDir\Network\Tier1\ip_address_network_card_*.png" = "ip_address"
    "$baseDir\Network\Tier1\binary_packet_network_card_*.png" = "binary_packet"
    "$baseDir\Network\Tier1\modem_network_card_*.png" = "modem"
    "$baseDir\Network\Tier1\switch_network_card_*.png" = "switch"
    "$baseDir\Network\Tier1\http_protocol_network_card_*.png" = "http_protocol"
    # Network Tier2
    "$baseDir\Network\Tier2\dhcp_server_network_card_*.png" = "dhcp_server"
    "$baseDir\Network\Tier2\dns_registry_network_card_*.png" = "dns_registry"
    "$baseDir\Network\Tier2\router_network_card_*.png" = "router"
    "$baseDir\Network\Tier2\tcp_ip_protocol_network_card_*.png" = "tcp_ip"
    "$baseDir\Network\Tier2\load_balancer_network_card_*.png" = "load_balancer"
    "$baseDir\Network\Tier2\web_socket_network_card_*.png" = "web_socket"
    # Network Tier3
    "$baseDir\Network\Tier3\cdn_network_card_*.png" = "cdn"
    "$baseDir\Network\Tier3\cloud_mesh_network_card_*.png" = "cloud_mesh"
    "$baseDir\Network\Tier3\optical_fiber_backbone_network_card_*.png" = "optical_fiber"
    "$baseDir\Network\Tier3\ipfs_network_card_*.png" = "ipfs"
    # Security Tier1
    "$baseDir\Security\Tier1\username_password_security_card_*.png" = "username_password"
    "$baseDir\Security\Tier1\antivirus_security_card_*.png" = "antivirus"
    "$baseDir\Security\Tier1\plaintext_security_card_*.png" = "plaintext"
    "$baseDir\Security\Tier1\access_token_security_card_*.png" = "access_token"
    "$baseDir\Security\Tier1\system_logs_security_card_*.png" = "system_logs"
    "$baseDir\Security\Tier1\port_blocking_security_card_*.png" = "port_blocking"
    "$baseDir\Security\Tier1\salting_password_security_card_*.png" = "salting_password"
    "$baseDir\Security\Tier1\symmetric_encryption_security_card_*.png" = "symmetric_encryption"
    # Security Tier2
    "$baseDir\Security\Tier2\asymmetric_key_rsa_security_card_*.png" = "asymmetric_rsa"
    "$baseDir\Security\Tier2\firewall_rules_security_card_*.png" = "firewall_rules"
    "$baseDir\Security\Tier2\https_ssl_security_card_*.png" = "https_ssl"
    "$baseDir\Security\Tier2\ddos_mitigation_security_card_*.png" = "ddos_mitigation"
    "$baseDir\Security\Tier2\two_factor_auth_security_card_*.png" = "two_factor_auth"
    "$baseDir\Security\Tier2\pentest_security_card_*.png" = "penetration_testing"
    # Security Tier3
    "$baseDir\Security\Tier3\jwt_token_security_card_*.png" = "jwt"
    "$baseDir\Security\Tier3\blockchain_ledger_security_card_*.png" = "blockchain_ledger"
    "$baseDir\Security\Tier3\end_to_end_encryption_security_card_*.png" = "end_to_end_encryption"
    "$baseDir\Security\Tier3\Zero Trust Architecture.png" = "zero_trust"
}

$renamedCount = 0
$errorCount = 0

foreach ($pattern in $renameMap.Keys) {
    $newName = $renameMap[$pattern]
    $files = Get-ChildItem -Path $pattern -ErrorAction SilentlyContinue
    
    if ($files) {
        foreach ($file in $files) {
            $dir = $file.DirectoryName
            $ext = $file.Extension
            $newFullName = Join-Path $dir "$newName$ext"
            
            # Skip if already named correctly
            if ($file.Name -eq "$newName$ext") {
                Write-Host "SKIP (already correct): $($file.Name)"
                $renamedCount++
                continue
            }
            
            try {
                Rename-Item -Path $file.FullName -NewName "$newName$ext" -ErrorAction Stop
                Write-Host "OK: $($file.Name) -> $newName$ext"
                $renamedCount++
                
                # Also rename .meta file if exists
                $metaFile = "$($file.FullName).meta"
                if (Test-Path $metaFile) {
                    Rename-Item -Path $metaFile -NewName "$newName$ext.meta" -ErrorAction SilentlyContinue
                    Write-Host "  META: renamed"
                }
            } catch {
                Write-Host "ERROR: $($file.Name) - $_"
                $errorCount++
            }
        }
    } else {
        Write-Host "NOT FOUND: $pattern"
        $errorCount++
    }
}

Write-Host "`n=== DONE: Renamed $renamedCount files, $errorCount errors ==="
