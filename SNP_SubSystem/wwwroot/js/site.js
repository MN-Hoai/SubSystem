$(document).ready(function () {

    /* ============================================================
       A) THAM CHIẾU DOM
       ============================================================ */

    const $sidebar = $('#sidebar');
    const $appMain = $('#appMain');
    const $toggleBtn = $('#sidebarToggle');
    const $collapseBtn = $('#collapseToggle');
    const $backdrop = $('#backdrop');


    // true nếu đang ở breakpoint tablet/mobile
    function isMobile() {
        return $(window).width() <= 1024;
    }


    /* ============================================================
       B) TOGGLE SIDEBAR
       - Desktop: thu/mở icon-only
       - Mobile: bật/tắt sidebar + backdrop
       ============================================================ */

    $toggleBtn.on('click', function () {

        if (isMobile()) {

            $sidebar.toggleClass('mobile-open');
            $backdrop.toggleClass('show');

        } else {

            $sidebar.toggleClass('is-collapsed');
            $appMain.toggleClass('is-collapsed');

        }

    });


    /* ============================================================
       C) NÚT "THU GỌN THANH BÊN"
       Chỉ dùng trên desktop
       ============================================================ */

    $collapseBtn.on('click', function () {

        $sidebar.toggleClass('is-collapsed');
        $appMain.toggleClass('is-collapsed');

    });


    /* ============================================================
       D) ĐÓNG SIDEBAR MOBILE KHI BẤM BACKDROP
       ============================================================ */

    $backdrop.on('click', function () {

        $sidebar.removeClass('mobile-open');
        $backdrop.removeClass('show');

    });


    /* ============================================================
       E) TRẠNG THÁI ACTIVE CỦA NAV ITEM
       ============================================================ */

    $('.nav-item').on('click', function () {

        // Bỏ active tất cả menu
        $('.nav-item').removeClass('active');

        // Active menu vừa click
        $(this).addClass('active');


        // Nếu đang ở mobile thì đóng sidebar
        if (isMobile()) {

            $sidebar.removeClass('mobile-open');
            $backdrop.removeClass('show');

        }

        // KHÔNG dùng e.preventDefault()
        // để link ASP.NET vẫn chuyển trang

    });


    /* ============================================================
       F) ĐỒNG BỘ KHI RESIZE
       ============================================================ */

    $(window).on('resize', function () {

        if (!isMobile()) {

            $sidebar.removeClass('mobile-open');
            $backdrop.removeClass('show');

        }

    });
    /* ============================================================
   E) NAV ITEM
   ============================================================ */

    // Khi click menu
    $('.nav-item').on('click', function () {

        $('.nav-item').removeClass('active');

        $(this).addClass('active');

        if (isMobile()) {
            $sidebar.removeClass('mobile-open');
            $backdrop.removeClass('show');
        }

        // Không dùng preventDefault()
        // Cho phép <a> chuyển trang
    });


    /* ============================================================
       TỰ ĐỘNG ACTIVE THEO URL SAU KHI LOAD TRANG
       ============================================================ */

    function normalizePath(path) {

        if (!path) {
            return '';
        }

        path = path.toLowerCase();

        // Xóa dấu / cuối
        if (path.length > 1 && path.endsWith('/')) {
            path = path.slice(0, -1);
        }

        return path;
    }


    const currentPath = normalizePath(window.location.pathname);


    $('.nav-item').each(function () {

        const linkPath = normalizePath($(this).attr('href'));

        if (linkPath === currentPath) {

            $('.nav-item').removeClass('active');

            $(this).addClass('active');

        }

    });
});