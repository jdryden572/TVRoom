import './root.css';

const userMenuButton = document.querySelector('.signed-in-user');
const userMenuDropdown = document.querySelector('.signed-in-user-details');
let showDropdown = false;

userMenuButton.addEventListener('click', () => {
    showDropdown = !showDropdown;
    if (showDropdown) {
        userMenuDropdown.style.display = 'block';
    } else {
        userMenuDropdown.style.display = 'none';
    }
})