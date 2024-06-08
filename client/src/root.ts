import './root.css';

const userMenuButton = document.querySelector('.signed-in-user') as HTMLElement;
const userMenuDropdown = document.querySelector('.signed-in-user-details') as HTMLElement;
let showDropdown = false;

userMenuButton.addEventListener('click', () => {
    showDropdown = !showDropdown;
    if (showDropdown) {
        userMenuDropdown.style.display = 'block';
    } else {
        userMenuDropdown.style.display = 'none';
    }
})