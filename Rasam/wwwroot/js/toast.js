    function closeToast() {
        var toast = document.getElementById('systemToast');
        if (toast) {
            toast.classList.add('toast-hide');
            setTimeout(function () {
                toast.remove();
            }, 400);
        }
    }

    document.addEventListener('DOMContentLoaded', function () {
        var toast = document.getElementById('systemToast');
        var progressBar = document.getElementById('toastProgress');
        
        if (toast && progressBar) {
            var duration = 4000; // مدت زمان کل نمایش (۴ ثانیه)
            var intervalTime = 20; // بازه زمانی به‌روزرسانی نوار
            var elapsed = 0;

            var timer = setInterval(function () {
                elapsed += intervalTime;
                var percentage = 100 - (elapsed / duration) * 100;
                
                if (percentage <= 0) {
                    progressBar.style.width = '0%';
                    clearInterval(timer);
                    closeToast();
                } else {
                    progressBar.style.width = percentage + '%';
                }
            }, intervalTime);
        }
    });
