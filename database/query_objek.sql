DROP VIEW IF EXISTS vw_laporan_peminjaman;
DROP TRIGGER IF EXISTS trg_detail_unit_peminjaman_after_insert;
DROP TRIGGER IF EXISTS trg_detail_pengembalian_after_insert;
DROP TRIGGER IF EXISTS trg_detail_pengembalian_after_update;
DROP PROCEDURE IF EXISTS sp_setujui_peminjaman;
DROP PROCEDURE IF EXISTS sp_terapkan_status_unit;
DROP FUNCTION IF EXISTS fn_hitung_denda_kondisi;

DELIMITER $$

CREATE FUNCTION fn_hitung_denda_kondisi(
    p_nama_kondisi VARCHAR(30),
    p_harga DECIMAL(15,2)
)
RETURNS DECIMAL(15,2)
DETERMINISTIC
NO SQL
BEGIN
    RETURN ROUND(
        CASE p_nama_kondisi
            WHEN 'baik' THEN 0
            WHEN 'rusak_ringan' THEN p_harga * 0.50
            WHEN 'rusak_berat' THEN p_harga * 2.00
            WHEN 'hilang' THEN p_harga * 5.00
            ELSE 0
        END,
        2
    );
END$$

CREATE PROCEDURE sp_terapkan_status_unit(
    IN p_id_unit_alat INT UNSIGNED,
    IN p_nama_kondisi VARCHAR(30)
)
BEGIN
    UPDATE unit_alat
    SET id_status_ketersediaan = (
            SELECT id
            FROM status_ketersediaan
            WHERE nama = CASE
                WHEN p_nama_kondisi IN ('baik', 'rusak_ringan') THEN 'tersedia'
                ELSE 'tidak_tersedia'
            END
            LIMIT 1
        ),
        id_status_barang = CASE p_nama_kondisi
            WHEN 'baik' THEN (
                SELECT id FROM status_barang WHERE nama = 'baik' LIMIT 1
            )
            WHEN 'rusak_ringan' THEN (
                SELECT id FROM status_barang WHERE nama = 'rusak_ringan' LIMIT 1
            )
            WHEN 'rusak_berat' THEN (
                SELECT id FROM status_barang WHERE nama = 'rusak_berat' LIMIT 1
            )
            ELSE id_status_barang
        END
    WHERE id = p_id_unit_alat;
END$$

CREATE TRIGGER trg_detail_unit_peminjaman_after_insert
AFTER INSERT ON detail_unit_peminjaman
FOR EACH ROW
BEGIN
    UPDATE unit_alat
    SET id_status_ketersediaan = (
        SELECT id
        FROM status_ketersediaan
        WHERE nama = 'dipinjam'
        LIMIT 1
    )
    WHERE id = NEW.id_unit_alat;
END$$

CREATE TRIGGER trg_detail_pengembalian_after_insert
AFTER INSERT ON detail_pengembalian
FOR EACH ROW
BEGIN
    DECLARE v_kondisi VARCHAR(30);

    SELECT nama
    INTO v_kondisi
    FROM status_kondisi_kembali
    WHERE id = NEW.id_status_kondisi_kembali;

    CALL sp_terapkan_status_unit(NEW.id_unit_alat, v_kondisi);
END$$

CREATE TRIGGER trg_detail_pengembalian_after_update
AFTER UPDATE ON detail_pengembalian
FOR EACH ROW
BEGIN
    DECLARE v_kondisi VARCHAR(30);

    SELECT nama
    INTO v_kondisi
    FROM status_kondisi_kembali
    WHERE id = NEW.id_status_kondisi_kembali;

    CALL sp_terapkan_status_unit(NEW.id_unit_alat, v_kondisi);
END$$

CREATE PROCEDURE sp_setujui_peminjaman(
    IN p_id_peminjaman INT UNSIGNED,
    IN p_id_petugas INT UNSIGNED
)
BEGIN
    DECLARE v_selesai BOOLEAN DEFAULT FALSE;
    DECLARE v_detail_id INT UNSIGNED;
    DECLARE v_alat_id INT UNSIGNED;
    DECLARE v_jumlah INT UNSIGNED;
    DECLARE v_urutan INT UNSIGNED;
    DECLARE v_unit_id INT UNSIGNED;
    DECLARE v_status VARCHAR(30);
    DECLARE v_jumlah_detail INT DEFAULT 0;
    DECLARE v_petugas_valid INT DEFAULT 0;

    DECLARE cur_detail CURSOR FOR
        SELECT id, id_alat, jumlah
        FROM detail_peminjaman
        WHERE id_peminjaman = p_id_peminjaman
        ORDER BY id;

    DECLARE CONTINUE HANDLER FOR NOT FOUND SET v_selesai = TRUE;
    DECLARE EXIT HANDLER FOR SQLEXCEPTION
    BEGIN
        ROLLBACK;
        RESIGNAL;
    END;

    START TRANSACTION;

    SET v_status = NULL;
    BEGIN
        DECLARE CONTINUE HANDLER FOR NOT FOUND SET v_status = NULL;

        SELECT sp.nama
        INTO v_status
        FROM peminjaman p
        INNER JOIN status_peminjaman sp ON sp.id = p.id_status_peminjaman
        WHERE p.id = p_id_peminjaman
          AND p.deleted_at IS NULL
        LIMIT 1
        FOR UPDATE;
    END;

    IF v_status IS NULL THEN
        SIGNAL SQLSTATE '45000'
            SET MESSAGE_TEXT = 'Peminjaman tidak ditemukan.';
    END IF;

    IF v_status <> 'diajukan' THEN
        SIGNAL SQLSTATE '45000'
            SET MESSAGE_TEXT = 'Peminjaman hanya dapat disetujui dari status diajukan.';
    END IF;

    SELECT COUNT(*)
    INTO v_petugas_valid
    FROM users u
    INNER JOIN roles r ON r.id = u.id_role
    WHERE u.id = p_id_petugas
      AND u.deleted_at IS NULL
      AND r.nama = 'Petugas';

    IF v_petugas_valid = 0 THEN
        SIGNAL SQLSTATE '45000'
            SET MESSAGE_TEXT = 'Petugas aktif tidak ditemukan.';
    END IF;

    SELECT COUNT(*)
    INTO v_jumlah_detail
    FROM detail_peminjaman
    WHERE id_peminjaman = p_id_peminjaman;

    IF v_jumlah_detail = 0 THEN
        SIGNAL SQLSTATE '45000'
            SET MESSAGE_TEXT = 'Detail peminjaman tidak ditemukan.';
    END IF;

    SET v_selesai = FALSE;
    OPEN cur_detail;

    loop_detail: LOOP
        FETCH cur_detail INTO v_detail_id, v_alat_id, v_jumlah;
        IF v_selesai THEN
            LEAVE loop_detail;
        END IF;

        SET v_urutan = 0;
        WHILE v_urutan < v_jumlah DO
            SET v_unit_id = NULL;

            BEGIN
                DECLARE CONTINUE HANDLER FOR NOT FOUND SET v_unit_id = NULL;

                SELECT ua.id
                INTO v_unit_id
                FROM unit_alat ua
                INNER JOIN alat a ON a.id = ua.id_alat
                INNER JOIN status_ketersediaan sk
                    ON sk.id = ua.id_status_ketersediaan
                WHERE ua.id_alat = v_alat_id
                  AND ua.deleted_at IS NULL
                  AND a.deleted_at IS NULL
                  AND sk.nama = 'tersedia'
                ORDER BY ua.id
                LIMIT 1
                FOR UPDATE;
            END;

            IF v_unit_id IS NULL THEN
                SIGNAL SQLSTATE '45000'
                    SET MESSAGE_TEXT = 'Unit tersedia tidak mencukupi untuk persetujuan.';
            END IF;

            INSERT INTO detail_unit_peminjaman (
                id_detail_peminjaman,
                id_unit_alat
            )
            VALUES (v_detail_id, v_unit_id);

            SET v_urutan = v_urutan + 1;
        END WHILE;
    END LOOP;

    CLOSE cur_detail;

    UPDATE peminjaman
    SET id_petugas = p_id_petugas,
        id_status_peminjaman = (
            SELECT id
            FROM status_peminjaman
            WHERE nama = 'disetujui'
            LIMIT 1
        )
    WHERE id = p_id_peminjaman
      AND deleted_at IS NULL;

    COMMIT;
END$$

DELIMITER ;

CREATE VIEW vw_laporan_peminjaman AS
SELECT
    p.id AS id_peminjaman,
    p.tanggal_peminjaman,
    p.tanggal_rencana_kembali,
    p.total_biaya,
    p.created_at,
    p.deleted_at AS peminjaman_deleted_at,
    peminjam.id AS id_peminjam,
    peminjam.username AS username_peminjam,
    peminjam.nama AS nama_peminjam,
    petugas.id AS id_petugas,
    petugas.nama AS nama_petugas,
    sp.nama AS status_peminjaman,
    a.id AS id_alat,
    a.nama AS nama_alat,
    k.nama AS nama_kategori,
    dp.jumlah,
    dp.harga_sewa_saat_transaksi,
    dp.jumlah * dp.harga_sewa_saat_transaksi AS subtotal_per_hari,
    GROUP_CONCAT(
        DISTINCT ua.kode_inventaris
        ORDER BY ua.kode_inventaris
        SEPARATOR ', '
    ) AS kode_unit,
    pg.tanggal_kembali,
    pg.keterlambatan_pengembalian,
    pg.denda,
    pg.deleted_at AS pengembalian_deleted_at
FROM peminjaman p
INNER JOIN users peminjam ON peminjam.id = p.id_peminjam
LEFT JOIN users petugas ON petugas.id = p.id_petugas
INNER JOIN status_peminjaman sp ON sp.id = p.id_status_peminjaman
INNER JOIN detail_peminjaman dp ON dp.id_peminjaman = p.id
INNER JOIN alat a ON a.id = dp.id_alat
INNER JOIN kategori k ON k.id = a.id_kategori
LEFT JOIN detail_unit_peminjaman dup
    ON dup.id_detail_peminjaman = dp.id
LEFT JOIN unit_alat ua ON ua.id = dup.id_unit_alat
LEFT JOIN pengembalian pg ON pg.id_peminjaman = p.id
GROUP BY
    p.id,
    p.tanggal_peminjaman,
    p.tanggal_rencana_kembali,
    p.total_biaya,
    p.created_at,
    p.deleted_at,
    peminjam.id,
    peminjam.username,
    peminjam.nama,
    petugas.id,
    petugas.nama,
    sp.nama,
    a.id,
    a.nama,
    k.nama,
    dp.jumlah,
    dp.harga_sewa_saat_transaksi,
    pg.tanggal_kembali,
    pg.keterlambatan_pengembalian,
    pg.denda,
    pg.deleted_at;
